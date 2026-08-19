using System.Globalization;
using System.Text.Json;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Services;

public class IncidentReportService : IIncidentReportService
{
    private const string ReportedTemplateId = "INCIDENT_REPORTED";
    private const string ExpenseApprovedTemplateId = "INCIDENT_EXPENSE_APPROVED";
    private const string ReimbursedTemplateId = "INCIDENT_REIMBURSED";
    private const string ResolvedTemplateId = "INCIDENT_RESOLVED";
    private const string SlaEscalatedTemplateId = "INCIDENT_SLA_ESCALATED";
    private const int MaxEvidenceFiles = 5;
    private const long MaxEvidenceFileSize = 10 * 1024 * 1024;
    private const decimal DefaultTemperatureTolerance = 2m;
    private const int DefaultReportedSlaMinutes = 15;
    private const int TrustedReadingMaxAgeMinutes = 30;

    private static readonly string[] IncidentRecipientRoles = { "ADMIN", "DISPATCHER" };
    private static readonly string[] AllowedEvidenceTypes =
    {
        "INCIDENT_ATTACHMENT",
        "INCIDENT_PHOTO",
        "DRIVER_RECEIPT"
    };

    private readonly IApplicationDbContext _db;
    private readonly IPdfGeneratorService _pdfGeneratorService;
    private readonly IFileService _fileService;
    private readonly ILogger<IncidentReportService> _logger;
    private readonly IIncidentRealtimeNotifier? _realtimeNotifier;
    private readonly INotificationService? _notificationService;
    private readonly IRealtimeTelemetryService? _realtimeTelemetryService;
    private readonly int _reportedSlaMinutes;

    public IncidentReportService(
        IApplicationDbContext db,
        IPdfGeneratorService pdfGeneratorService,
        IFileService fileService,
        ILogger<IncidentReportService> logger,
        IIncidentRealtimeNotifier? realtimeNotifier = null,
        INotificationService? notificationService = null,
        IRealtimeTelemetryService? realtimeTelemetryService = null,
        IConfiguration? configuration = null)
    {
        _db = db;
        _pdfGeneratorService = pdfGeneratorService;
        _fileService = fileService;
        _logger = logger;
        _realtimeNotifier = realtimeNotifier;
        _notificationService = notificationService;
        _realtimeTelemetryService = realtimeTelemetryService;
        _reportedSlaMinutes = Math.Max(
            1,
            configuration?.GetValue<int?>("IncidentWorkflow:ReportedSlaMinutes")
            ?? DefaultReportedSlaMinutes);
    }

    public async Task<ApiResponse<IncidentResponse>> ReportIncidentAsync(
        CreateIncidentRequest request,
        Guid userId)
    {
        if (request == null)
            return ApiResponse<IncidentResponse>.Failure("Request is null.");
        if (!request.IncidentType.HasValue)
            return ApiResponse<IncidentResponse>.Failure("Incident type is required.");
        if (!request.Severity.HasValue && !request.RiskLevel.HasValue)
            return ApiResponse<IncidentResponse>.Failure("Severity or RiskLevel is required.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return ApiResponse<IncidentResponse>.Failure("Description is required.");
        if (request.DriverPaidAmount < 0)
            return ApiResponse<IncidentResponse>.Failure("Driver-paid amount cannot be negative.");
        if (request.CurrentLatitude is < -90m or > 90m)
            return ApiResponse<IncidentResponse>.Failure("Current latitude must be between -90 and 90.");
        if (request.CurrentLongitude is < -180m or > 180m)
            return ApiResponse<IncidentResponse>.Failure("Current longitude must be between -180 and 180.");

        var files = request.EvidenceFiles?.Where(f => f != null).ToList() ?? new List<IFormFile>();
        var fileValidation = ValidateEvidenceFiles(files, allowEmpty: true);
        if (fileValidation != null)
            return ApiResponse<IncidentResponse>.Failure(fileValidation);

        try
        {
            var reporter = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId);
            if (reporter == null)
                return ApiResponse<IncidentResponse>.Failure("Reporter user not found.");

            MasterTrip? trip = null;
            if (request.TripId.HasValue)
            {
                trip = await _db.MasterTrips
                    .Include(t => t.Vehicle)
                        .ThenInclude(v => v!.IotDevices)
                    .FirstOrDefaultAsync(t => t.TripId == request.TripId.Value);
                if (trip == null)
                    return ApiResponse<IncidentResponse>.Failure("Trip not found.");
            }

            var previousIncident = request.TripId.HasValue
                ? await _db.IncidentReports
                    .Where(i => i.TripId == request.TripId.Value)
                    .OrderByDescending(i => i.ReportedAt)
                    .FirstOrDefaultAsync()
                : null;

            var isDriver = string.Equals(
                               reporter.Role?.RoleName,
                               "Driver",
                               StringComparison.OrdinalIgnoreCase) ||
                           await _db.Drivers.AnyAsync(d => d.UserId == userId);
            if (isDriver)
            {
                if (!request.TripId.HasValue)
                    return ApiResponse<IncidentResponse>.Failure("Driver incident reports must be linked to a trip.");

                var assignedToTrip = await _db.TripDrivers.AnyAsync(td =>
                    td.TripId == request.TripId.Value &&
                    td.Driver.UserId == userId);
                if (!assignedToTrip)
                    return ApiResponse<IncidentResponse>.Failure("Driver is not assigned to this trip.", 403);
            }

            var resolvedLocation = await ResolveIncidentVehicleLocationAsync(
                trip,
                isDriver ? null : request.CurrentLatitude,
                isDriver ? null : request.CurrentLongitude);
            if (isDriver && (!resolvedLocation.Latitude.HasValue || !resolvedLocation.Longitude.HasValue))
            {
                return ApiResponse<IncidentResponse>.Failure(
                    "Vehicle telemetry location is required for driver incident reports. No realtime or persisted IoT GPS position was found for this trip.");
            }

            var uploadedEvidences = new List<(string Type, string Url)>();
            foreach (var file in files)
            {
                var url = await _fileService.UploadFileAsync(file);
                uploadedEvidences.Add((InferEvidenceType(file), url));
            }

            var now = DbNow();
            var riskLevel = request.RiskLevel ?? MapLegacySeverityToRisk(request.Severity);
            if (previousIncident?.ReplacementVehicleId == trip?.VehicleId
                && (riskLevel == IncidentRiskLevel.WARNING || request.RequiresRescue))
            {
                riskLevel = IncidentRiskLevel.CRITICAL;
            }
            var incident = new IncidentReport
            {
                IncidentId = Guid.NewGuid(),
                TripId = request.TripId,
                IncidentType = request.IncidentType.Value.ToString(),
                Severity = (request.Severity ?? MapRiskToLegacySeverity(riskLevel)).ToString(),
                RiskLevel = riskLevel.ToString(),
                Description = request.Description.Trim(),
                CurrentLatitude = resolvedLocation.Latitude,
                CurrentLongitude = resolvedLocation.Longitude,
                DriverPaidAmount = request.DriverPaidAmount,
                RequiresRescue = request.RequiresRescue,
                TemperatureTolerance = DefaultTemperatureTolerance,
                PreviousIncidentId = previousIncident?.IncidentId,
                SlaDueAt = now.AddMinutes(_reportedSlaMinutes),
                ExpenseStatus = request.DriverPaidAmount > 0 ? "PENDING_APPROVAL" : "NOT_REQUIRED",
                Status = "REPORTED",
                ReportedBy = userId,
                ReportedAt = now,
                BrokenVehicleId = trip?.VehicleId
            };

            foreach (var uploaded in uploadedEvidences)
            {
                incident.IncidentEvidences.Add(new IncidentEvidence
                {
                    EvidenceId = Guid.NewGuid(),
                    IncidentId = incident.IncidentId,
                    EvidenceType = uploaded.Type,
                    FileUrl = uploaded.Url
                });
            }

            _db.IncidentReports.Add(incident);

            var recipientIds = await _db.Users
                .Where(u => u.Role != null &&
                            IncidentRecipientRoles.Contains(u.Role.RoleName.ToUpper()))
                .Select(u => u.UserId)
                .ToListAsync();

            if (_notificationService == null)
            {
                var templateId = await EnsureNotificationTemplateAsync(
                    ReportedTemplateId,
                    "Sự cố mới trên chuyến {{trip_id}}",
                    "{{reporter_name}} vừa báo sự cố {{incident_type}} mức rủi ro {{risk_level}}. Yêu cầu cứu hàng: {{requires_rescue}}.");

                var parameters = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["incident_id"] = incident.IncidentId.ToString(),
                    ["trip_id"] = incident.TripId?.ToString() ?? "N/A",
                    ["reporter_name"] = reporter.FullName,
                    ["incident_type"] = incident.IncidentType,
                    ["severity"] = incident.Severity,
                    ["risk_level"] = incident.RiskLevel ?? incident.Severity,
                    ["requires_rescue"] = incident.RequiresRescue ? "Có" : "Không"
                });

                if (templateId != null)
                {
                    foreach (var recipientId in recipientIds.Distinct())
                    {
                        _db.Notifications.Add(new Notification
                        {
                            NotiId = Guid.NewGuid(),
                            UserId = recipientId,
                            SenderId = userId,
                            TemplateId = templateId,
                            Params = parameters,
                            IsRead = false,
                            CreatedAt = now
                        });
                    }
                }
            }

            if (incident.TripId.HasValue)
            {
                var remainingStops = await _db.TripStops
                    .Where(s => s.TripId == incident.TripId.Value && s.ActualArrivalTime == null)
                    .ToListAsync();

                Guid? targetStopId = null;
                foreach (var stop in remainingStops)
                {
                    stop.Status = "DELAYED_INCIDENT";
                    stop.Note = $"{stop.Note} [Sự cố cung đường: {incident.IncidentType} ({incident.Description})]".Trim();
                    if (targetStopId == null) targetStopId = stop.StopId;
                }

                if (targetStopId == null && trip != null)
                {
                    var firstStop = await _db.TripStops.FirstOrDefaultAsync(s => s.TripId == trip.TripId);
                    if (firstStop != null) targetStopId = firstStop.StopId;
                }

                var customerTemplateId = await EnsureNotificationTemplateAsync(
                    "INCIDENT_AUTO_ETA",
                    "⚠️ Thông báo sự cố trong quá trình vận chuyển: Chuyến {{trip_code}}",
                    "Xe vận chuyển chuỗi lạnh gặp sự cố ({{incident_type}}: {{description}}) trên đường đi; lộ trình giao có thể bị gián đoạn hoặc trễ. Đội điều phối đang xác minh điều kiện nhiệt độ và sẽ cập nhật phương án an toàn.");

                if (customerTemplateId != null)
                {
                    var customerUserIds = await (
                        from o in _db.TransportOrders
                        where o.MasterTripId == incident.TripId.Value && o.CustomerId != null
                        join c in _db.Customers on o.CustomerId equals c.CustomerId
                        where c.Email != null && c.Email != ""
                        join u in _db.Users on c.Email!.ToLower() equals u.Email!.ToLower()
                        select u.UserId
                    ).Distinct().ToListAsync();

                    var custParams = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["trip_code"] = incident.TripId.Value.ToString(),
                        ["incident_type"] = incident.IncidentType,
                        ["description"] = incident.Description
                    });

                    foreach (var custId in customerUserIds.Distinct())
                    {
                        _db.Notifications.Add(new Notification
                        {
                            NotiId = Guid.NewGuid(),
                            UserId = custId,
                            SenderId = userId,
                            TemplateId = customerTemplateId,
                            Params = custParams,
                            IsRead = false,
                            CreatedAt = now
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();

            if (_notificationService != null && recipientIds.Count > 0)
            {
                try
                {
                    var pushResult = await _notificationService.SendToUsersAsync(
                        recipientIds,
                        "Tài xế vừa báo cáo sự cố",
                        "Một sự cố mới vừa được ghi nhận trên chuyến vận chuyển.",
                        "INCIDENT_CREATED",
                        incident.IncidentId.ToString(),
                        new Dictionary<string, string>
                        {
                            ["incidentId"] = incident.IncidentId.ToString(),
                            ["tripId"] = incident.TripId?.ToString() ?? string.Empty,
                            ["screen"] = "incident-detail"
                        });

                    if (pushResult.FailedSends > 0)
                    {
                        _logger.LogWarning(
                            "Incident FCM notification was partially delivered. IncidentId: {IncidentId}, Successful: {Successful}, Failed: {Failed}.",
                            incident.IncidentId,
                            pushResult.SuccessfulSends,
                            pushResult.FailedSends);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Incident FCM notification failed after the incident was saved. IncidentId: {IncidentId}.",
                        incident.IncidentId);
                }
            }

            await SafeNotifyGroupsAsync(
                new[] { "Group_Dispatcher", "Group_Admin", "Group_Customer" },
                "IncidentReported",
                new
                {
                    incident.IncidentId,
                    incident.TripId,
                    incident.IncidentType,
                    incident.Severity,
                    incident.RiskLevel,
                    incident.Description,
                    incident.CurrentLatitude,
                    incident.CurrentLongitude,
                    incident.DriverPaidAmount,
                    incident.RequiresRescue,
                    EvidenceCount = uploadedEvidences.Count,
                    ReporterId = reporter.UserId,
                    ReporterName = reporter.FullName,
                    incident.ReportedAt
                });

            var savedIncident = await LoadIncidentAsync(incident.IncidentId);
            return ApiResponse<IncidentResponse>.SuccessResponse(
                MapToResponse(savedIncident!),
                "Incident reported successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report incident. TripId: {TripId}", request.TripId);
            return ApiResponse<IncidentResponse>.Failure($"Failed to report incident: {ex.Message}");
        }
    }

    public async Task<ApiResponse<IncidentResponse>> AddEvidenceAsync(
        Guid incidentId,
        IReadOnlyCollection<IFormFile> files,
        string evidenceType,
        Guid userId)
    {
        var normalizedType = string.IsNullOrWhiteSpace(evidenceType)
            ? "INCIDENT_ATTACHMENT"
            : evidenceType.Trim().ToUpperInvariant();
        if (!AllowedEvidenceTypes.Contains(normalizedType))
            return ApiResponse<IncidentResponse>.Failure(
                $"EvidenceType must be one of: {string.Join(", ", AllowedEvidenceTypes)}.");

        var fileValidation = ValidateEvidenceFiles(files);
        if (fileValidation != null)
            return ApiResponse<IncidentResponse>.Failure(fileValidation);

        try
        {
            var incident = await LoadIncidentAsync(incidentId);
            if (incident == null)
                return ApiResponse<IncidentResponse>.Failure("Incident not found.", 404);
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentResponse>.Failure("Cannot add evidence to a resolved incident.");

            var actor = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            if (actor == null)
                return ApiResponse<IncidentResponse>.Failure("Evidence uploader user not found.", 404);

            var isPrivileged = actor?.Role?.RoleName is not null &&
                               (actor.Role.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                                actor.Role.RoleName.Equals("Dispatcher", StringComparison.OrdinalIgnoreCase));
            if (incident.ReportedBy != userId && !isPrivileged)
                return ApiResponse<IncidentResponse>.Failure("You cannot add evidence to this incident.", 403);

            foreach (var file in files)
            {
                var url = await _fileService.UploadFileAsync(file);
                _db.IncidentEvidences.Add(new IncidentEvidence
                {
                    EvidenceId = Guid.NewGuid(),
                    IncidentId = incidentId,
                    EvidenceType = normalizedType,
                    FileUrl = url
                });
            }

            await _db.SaveChangesAsync();
            var saved = await LoadIncidentAsync(incidentId);
            return ApiResponse<IncidentResponse>.SuccessResponse(
                MapToResponse(saved!),
                "Incident evidence uploaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload incident evidence. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentResponse>.Failure($"Failed to upload incident evidence: {ex.Message}");
        }
    }

    public async Task<ApiResponse<IncidentRiskAssessmentResponse>> AssessRiskAsync(
        Guid incidentId,
        AssessIncidentRiskRequest request,
        Guid userId)
    {
        if (request == null)
            return ApiResponse<IncidentRiskAssessmentResponse>.Failure("Request is null.");
        if (!Enum.IsDefined(request.RiskLevel))
            return ApiResponse<IncidentRiskAssessmentResponse>.Failure("RiskLevel must be LOW, WARNING or CRITICAL.");
        if (!Enum.IsDefined(request.TemperatureSource))
            return ApiResponse<IncidentRiskAssessmentResponse>.Failure("TemperatureSource is invalid.");
        if (request.RiskLevel == IncidentRiskLevel.LOW && !request.CanSafelyRepairOnSite.HasValue)
        {
            return ApiResponse<IncidentRiskAssessmentResponse>.Failure(
                "CanSafelyRepairOnSite is required for LOW risk incidents.");
        }

        try
        {
            var incident = await _db.IncidentReports
                .Include(i => i.Trip)
                .Include(i => i.IncidentEvidences)
                .FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<IncidentRiskAssessmentResponse>.Failure("Incident not found.", 404);
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentRiskAssessmentResponse>.Failure("Incident is already resolved.");
            if (incident.Trip == null)
                return ApiResponse<IncidentRiskAssessmentResponse>.Failure(
                    "Risk assessment requires an incident linked to a MasterTrip.");
            if (!await _db.Users.AnyAsync(u => u.UserId == userId))
                return ApiResponse<IncidentRiskAssessmentResponse>.Failure("Assessor user not found.", 404);

            var now = DbNow();
            var recentTelemetry = await _db.TelemetryLogs
                .AsNoTracking()
                .Where(t => t.TripId == incident.TripId)
                .OrderByDescending(t => t.Timestamp)
                .Take(12)
                .ToListAsync();
            recentTelemetry.Reverse();

            decimal? measuredTemperature;
            DateTime? measuredAt;
            if (request.TemperatureSource == TemperatureReadingSource.IOT)
            {
                var latestTelemetry = recentTelemetry.LastOrDefault();
                measuredTemperature = latestTelemetry?.Temperature;
                measuredAt = latestTelemetry?.Timestamp;
            }
            else
            {
                measuredTemperature = request.MeasuredTemperature;
                measuredAt = request.MeasuredAt ?? (request.MeasuredTemperature.HasValue ? now : null);
            }

            var hasPhotoEvidence = incident.IncidentEvidences.Any(e =>
                e.EvidenceType.Contains("PHOTO", StringComparison.OrdinalIgnoreCase)
                || e.EvidenceType.Contains("IMAGE", StringComparison.OrdinalIgnoreCase));
            var readingAge = measuredAt.HasValue ? now - measuredAt.Value : TimeSpan.MaxValue;
            var hasTrustedSource = request.TemperatureSource != TemperatureReadingSource.NONE
                && measuredTemperature.HasValue
                && readingAge >= TimeSpan.Zero
                && readingAge <= TimeSpan.FromMinutes(TrustedReadingMaxAgeMinutes)
                && (request.TemperatureSource != TemperatureReadingSource.TIMESTAMPED_PHOTO || hasPhotoEvidence);

            var target = incident.Trip.TargetTemperature;
            var tolerance = incident.TemperatureTolerance > 0
                ? incident.TemperatureTolerance
                : DefaultTemperatureTolerance;
            var currentThresholdBreached = measuredTemperature.HasValue
                && (measuredTemperature.Value < target - tolerance
                    || measuredTemperature.Value > target + tolerance);
            var thresholdBreached = incident.TemperatureThresholdBreached || currentThresholdBreached;

            var effectiveRisk = request.RiskLevel;
            var reasons = new List<string>();
            if (request.RiskLevel == IncidentRiskLevel.WARNING && !hasTrustedSource)
            {
                effectiveRisk = IncidentRiskLevel.CRITICAL;
                reasons.Add("No recent trusted temperature reading is available.");
            }
            if (!request.TemperatureStable && request.RiskLevel != IncidentRiskLevel.CRITICAL)
            {
                effectiveRisk = IncidentRiskLevel.CRITICAL;
                reasons.Add("Temperature is not confirmed stable.");
            }
            if (currentThresholdBreached)
            {
                effectiveRisk = IncidentRiskLevel.CRITICAL;
                reasons.Add("The measured temperature is outside the MasterTrip target tolerance.");
            }
            else if (incident.TemperatureThresholdBreached)
            {
                effectiveRisk = IncidentRiskLevel.CRITICAL;
                reasons.Add("A temperature threshold breach was recorded earlier in this incident.");
            }
            if (incident.PreviousIncidentId.HasValue && effectiveRisk != IncidentRiskLevel.LOW)
            {
                effectiveRisk = IncidentRiskLevel.CRITICAL;
                reasons.Add("This is a repeated incident on the running replacement trip.");
            }

            var (remainingSafeMinutes, safeTimeCalculation) = CalculateRemainingSafeTime(
                target,
                tolerance,
                measuredTemperature,
                measuredAt,
                recentTelemetry,
                thresholdBreached);

            incident.RiskLevel = effectiveRisk.ToString();
            incident.TemperatureSource = request.TemperatureSource.ToString();
            incident.LatestTemperature = measuredTemperature;
            incident.TemperatureMeasuredAt = measuredAt;
            incident.TemperatureTolerance = tolerance;
            incident.TemperatureThresholdBreached = thresholdBreached;
            incident.RemainingSafeTimeMinutes = remainingSafeMinutes;
            incident.SafeTimeCalculation = safeTimeCalculation;
            incident.HandledBy = userId;
            incident.HandledAt = now;
            if (!string.IsNullOrWhiteSpace(request.Note))
                incident.HandlingNote = request.Note.Trim();

            if (effectiveRisk == IncidentRiskLevel.CRITICAL)
            {
                incident.RequiresRescue = true;
                incident.DirectDeliveryLocked = thresholdBreached;
                if (request.ContainmentConfirmed)
                    incident.ContainmentConfirmedAt = now;

                incident.Status = request.ContainmentConfirmed
                    ? "RESCUE_PLANNING"
                    : "CONTAINMENT_REQUIRED";
                if (!request.ContainmentConfirmed)
                    reasons.Add("Cold containment must be confirmed before rescue handling starts.");
                else if (thresholdBreached)
                    reasons.Add("Temperature threshold was breached; direct delivery stays locked while rescue planning starts.");
            }
            else if (effectiveRisk == IncidentRiskLevel.WARNING)
            {
                incident.RequiresRescue = false;
                incident.DirectDeliveryLocked = false;
                incident.Status = "MONITORING";
                reasons.Add("A recent trusted reading confirms stable temperature; manual monitoring may continue.");
            }
            else
            {
                incident.RequiresRescue = request.CanSafelyRepairOnSite == false;
                incident.DirectDeliveryLocked = false;
                incident.Status = incident.RequiresRescue ? "RESCUE_PLANNING" : "TRIAGED";
                reasons.Add(incident.RequiresRescue
                    ? "The issue cannot be repaired safely on site; a cargo rescue plan is required."
                    : "Temperature is stable and the issue can be repaired safely on site.");
            }

            await _db.SaveChangesAsync();

            var response = new IncidentRiskAssessmentResponse
            {
                IncidentId = incident.IncidentId,
                RequestedRiskLevel = request.RiskLevel.ToString(),
                EffectiveRiskLevel = effectiveRisk.ToString(),
                IncidentStatus = incident.Status!,
                EscalatedToCritical = request.RiskLevel != IncidentRiskLevel.CRITICAL
                    && effectiveRisk == IncidentRiskLevel.CRITICAL,
                DecisionReason = string.Join(" ", reasons.Distinct()),
                TargetTemperature = target,
                TemperatureTolerance = tolerance,
                LatestTemperature = measuredTemperature,
                TemperatureMeasuredAt = measuredAt,
                TemperatureSource = request.TemperatureSource.ToString(),
                HasTrustedTemperatureSource = hasTrustedSource,
                TemperatureThresholdBreached = thresholdBreached,
                DirectDeliveryLocked = incident.DirectDeliveryLocked,
                RequiresRescue = incident.RequiresRescue,
                RemainingSafeTimeMinutes = remainingSafeMinutes,
                SafeTimeCalculation = safeTimeCalculation
            };

            await SafeNotifyGroupsAsync(
                new[] { "Group_Dispatcher", "Group_Admin" },
                "IncidentRiskAssessed",
                response);

            return ApiResponse<IncidentRiskAssessmentResponse>.SuccessResponse(
                response,
                "Incident temperature risk assessed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assess incident risk. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentRiskAssessmentResponse>.Failure(
                $"Failed to assess incident risk: {ex.Message}");
        }
    }

    public async Task<ApiResponse<IncidentResponse>> ApproveExpenseAsync(
        Guid incidentId,
        ApproveIncidentExpenseRequest request,
        Guid adminId)
    {
        if (request == null)
            return ApiResponse<IncidentResponse>.Failure("Request is null.");
        if (request.ApprovedAmount <= 0)
            return ApiResponse<IncidentResponse>.Failure("Approved amount must be greater than zero.");

        try
        {
            var incident = await LoadIncidentAsync(incidentId);
            if (incident == null)
                return ApiResponse<IncidentResponse>.Failure("Incident not found.", 404);
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentResponse>.Failure("Incident is already resolved.");
            if (incident.DriverPaidAmount <= 0)
                return ApiResponse<IncidentResponse>.Failure("This incident has no driver-paid expense to approve.");
            if (request.ApprovedAmount > incident.DriverPaidAmount)
                return ApiResponse<IncidentResponse>.Failure(
                    "Approved amount cannot exceed the amount paid by the driver.");
            if (!await _db.Users.AnyAsync(u => u.UserId == adminId))
                return ApiResponse<IncidentResponse>.Failure("Approver user not found.");

            var now = DbNow();
            incident.ApprovedAmount = request.ApprovedAmount;
            incident.ExpenseStatus = "APPROVED";
            incident.ExpenseApprovedBy = adminId;
            incident.ExpenseApprovedAt = now;
            incident.ExpenseApprovalNote = request.ApprovalNote?.Trim();

            if (_notificationService == null)
            {
                await AddUserNotificationAsync(
                    incident.ReportedBy,
                    adminId,
                    ExpenseApprovedTemplateId,
                    "Chi phí sự cố đã được duyệt",
                    "Khoản chi {{approved_amount}} VND cho sự cố {{incident_id}} đã được duyệt.",
                    new Dictionary<string, string>
                    {
                        ["incident_id"] = incident.IncidentId.ToString(),
                        ["approved_amount"] = request.ApprovedAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))
                    },
                    now);
            }

            await _db.SaveChangesAsync();

            if (_notificationService != null)
            {
                try
                {
                    var pushResult = await _notificationService.SendToUserAsync(
                        incident.ReportedBy,
                        "Chi phí đã được duyệt",
                        "Chi phí phát sinh của chuyến đi đã được phê duyệt.",
                        "EXPENSE_APPROVED",
                        incident.IncidentId.ToString(),
                        new Dictionary<string, string>
                        {
                            ["incidentId"] = incident.IncidentId.ToString(),
                            ["tripId"] = incident.TripId?.ToString() ?? string.Empty,
                            ["screen"] = "expense-detail"
                        });

                    if (pushResult.FailedSends > 0)
                    {
                        _logger.LogWarning(
                            "Expense approval FCM notification was not delivered to every device. IncidentId: {IncidentId}.",
                            incident.IncidentId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Expense approval FCM notification failed after the approval was saved. IncidentId: {IncidentId}.",
                        incident.IncidentId);
                }
            }

            await SafeNotifyUserAsync(incident.ReportedBy, "IncidentExpenseApproved", new
            {
                incident.IncidentId,
                incident.ApprovedAmount,
                incident.ExpenseStatus,
                incident.ExpenseApprovedAt
            });

            return ApiResponse<IncidentResponse>.SuccessResponse(
                MapToResponse(incident),
                "Incident expense approved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve incident expense. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentResponse>.Failure($"Failed to approve incident expense: {ex.Message}");
        }
    }

    public async Task<ApiResponse<IncidentResponse>> ReimburseExpenseAsync(
        Guid incidentId,
        ReimburseIncidentExpenseRequest request,
        Guid adminId)
    {
        if (request == null)
            return ApiResponse<IncidentResponse>.Failure("Request is null.");
        if (request.ReceiptFile == null)
            return ApiResponse<IncidentResponse>.Failure("Reimbursement receipt is required.");
        if (request.ReimbursedAmount <= 0)
            return ApiResponse<IncidentResponse>.Failure("Reimbursed amount must be greater than zero.");

        var fileValidation = ValidateEvidenceFiles(new[] { request.ReceiptFile });
        if (fileValidation != null)
            return ApiResponse<IncidentResponse>.Failure(fileValidation);

        try
        {
            var incident = await LoadIncidentAsync(incidentId);
            if (incident == null)
                return ApiResponse<IncidentResponse>.Failure("Incident not found.", 404);
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentResponse>.Failure("Incident is already resolved.");
            if (incident.ExpenseStatus != "APPROVED" || !incident.ApprovedAmount.HasValue)
                return ApiResponse<IncidentResponse>.Failure("Incident expense must be approved before reimbursement.");
            if (request.ReimbursedAmount != incident.ApprovedAmount.Value)
                return ApiResponse<IncidentResponse>.Failure(
                    "Reimbursed amount must equal the approved amount.");
            if (!await _db.Users.AnyAsync(u => u.UserId == adminId))
                return ApiResponse<IncidentResponse>.Failure("Reimburser user not found.");

            var receiptUrl = await _fileService.UploadFileAsync(request.ReceiptFile);
            var now = DbNow();

            incident.ReimbursedAmount = request.ReimbursedAmount;
            incident.ReimbursedBy = adminId;
            incident.ReimbursedAt = now;
            incident.ExpenseStatus = "REIMBURSED";
            if (!string.IsNullOrWhiteSpace(request.Note))
            {
                incident.ExpenseApprovalNote = string.IsNullOrWhiteSpace(incident.ExpenseApprovalNote)
                    ? request.Note.Trim()
                    : $"{incident.ExpenseApprovalNote} | Reimbursement: {request.Note.Trim()}";
            }

            _db.IncidentEvidences.Add(new IncidentEvidence
            {
                EvidenceId = Guid.NewGuid(),
                IncidentId = incident.IncidentId,
                EvidenceType = "REIMBURSEMENT_RECEIPT",
                FileUrl = receiptUrl
            });

            await AddUserNotificationAsync(
                incident.ReportedBy,
                adminId,
                ReimbursedTemplateId,
                "Đã hoàn tiền chi phí sự cố",
                "Đã hoàn {{reimbursed_amount}} VND cho sự cố {{incident_id}}. Biên lai: {{receipt_url}}",
                new Dictionary<string, string>
                {
                    ["incident_id"] = incident.IncidentId.ToString(),
                    ["reimbursed_amount"] = request.ReimbursedAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")),
                    ["receipt_url"] = receiptUrl
                },
                now);

            await _db.SaveChangesAsync();

            await SafeNotifyUserAsync(incident.ReportedBy, "IncidentExpenseReimbursed", new
            {
                incident.IncidentId,
                incident.ReimbursedAmount,
                incident.ExpenseStatus,
                ReceiptUrl = receiptUrl,
                incident.ReimbursedAt
            });

            var saved = await LoadIncidentAsync(incidentId);
            return ApiResponse<IncidentResponse>.SuccessResponse(
                MapToResponse(saved!),
                "Incident expense reimbursed and receipt sent to driver.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reimburse incident expense. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentResponse>.Failure($"Failed to reimburse incident expense: {ex.Message}");
        }
    }

    public async Task<ApiResponse<bool>> ResolveIncidentAsync(
        Guid incidentId,
        ResolveIncidentRequest request,
        Guid userId)
    {
        if (request == null)
            return ApiResponse<bool>.Failure("Request is null.");
        if (string.IsNullOrWhiteSpace(request.ResolutionNote))
            return ApiResponse<bool>.Failure("Resolution note is required.");

        try
        {
            var incident = await LoadIncidentAsync(incidentId);
            if (incident == null)
                return ApiResponse<bool>.Failure("Incident not found.", 404);
            if (incident.Status == "RESOLVED")
                return ApiResponse<bool>.Failure("Incident is already resolved.");

            if (incident.TripId.HasValue)
            {
                var operationallyReady = incident.RequiresRescue
                    ? incident.Status is "TRANSLOAD_COMPLETED" or "REDISPATCH_PLANNED"
                    : incident.Status == "CONTINUED";
                if (!operationallyReady)
                {
                    return ApiResponse<bool>.Failure(
                        incident.RequiresRescue
                            ? "A rescue incident can only be resolved after transload completion or a clear redispatch plan."
                            : "Incident can only be resolved after the trip has continued.");
                }
            }

            if (incident.DriverPaidAmount > 0 &&
                (incident.ExpenseStatus != "REIMBURSED" || !incident.ReimbursedAmount.HasValue))
            {
                return ApiResponse<bool>.Failure(
                    "Driver expense must be approved and reimbursed before resolving the incident.");
            }

            var resolver = await _db.Users.FindAsync(userId);
            if (resolver == null)
                return ApiResponse<bool>.Failure("Resolver user not found.");

            var resolvedAt = DbNow();
            var resolutionNote = request.ResolutionNote.Trim();
            var viCulture = CultureInfo.GetCultureInfo("vi-VN");
            var documentData = new
            {
                IncidentId = incident.IncidentId.ToString(),
                TripId = incident.TripId?.ToString() ?? "N/A",
                incident.IncidentType,
                incident.Severity,
                RiskLevel = incident.RiskLevel ?? incident.Severity,
                incident.Description,
                TargetTemperature = incident.Trip?.TargetTemperature.ToString("0.##", CultureInfo.InvariantCulture) ?? "N/A",
                LatestTemperature = incident.LatestTemperature?.ToString("0.##", CultureInfo.InvariantCulture) ?? "N/A",
                TemperatureThresholdBreached = incident.TemperatureThresholdBreached ? "Có" : "Không",
                RescuePlanType = incident.RescuePlanType ?? "N/A",
                ResolutionNote = resolutionNote,
                Location = FormatLocation(incident.CurrentLatitude, incident.CurrentLongitude),
                DriverPaidAmount = incident.DriverPaidAmount.ToString("N2", viCulture),
                ReimbursedAmount = (incident.ReimbursedAmount ?? 0m).ToString("N2", viCulture),
                ReporterName = incident.ReportedByNavigation?.FullName
                    ?? incident.ReportedByNavigation?.Username
                    ?? incident.ReportedBy.ToString(),
                ResolverName = resolver.FullName ?? resolver.Username,
                ReportedAt = FormatDateTime(incident.ReportedAt),
                ResolvedAt = FormatDateTime(resolvedAt)
            };

            var pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("IncidentResolution", documentData);
            var fileUrl = await _fileService.UploadFileAsync(
                pdfBytes,
                $"incident_resolution_{incident.IncidentId:N}.pdf");

            incident.Status = "RESOLVED";
            incident.ResolutionNote = resolutionNote;
            incident.ResolvedBy = userId;
            incident.ResolvedAt = resolvedAt;

            _db.IncidentEvidences.Add(new IncidentEvidence
            {
                EvidenceId = Guid.NewGuid(),
                IncidentId = incident.IncidentId,
                EvidenceType = "RESOLUTION_PDF",
                FileUrl = fileUrl
            });

            await AddUserNotificationAsync(
                incident.ReportedBy,
                userId,
                ResolvedTemplateId,
                "Sự cố đã được đóng",
                "Sự cố {{incident_id}} đã được giải quyết. Biên bản: {{resolution_url}}",
                new Dictionary<string, string>
                {
                    ["incident_id"] = incident.IncidentId.ToString(),
                    ["resolution_url"] = fileUrl
                },
                resolvedAt);

            await _db.SaveChangesAsync();

            await SafeNotifyUserAsync(incident.ReportedBy, "IncidentResolved", new
            {
                incident.IncidentId,
                incident.TripId,
                incident.Status,
                ResolutionUrl = fileUrl,
                incident.ResolvedAt
            });
            await SafeNotifyGroupsAsync(
                new[] { "Group_Dispatcher", "Group_Admin" },
                "IncidentResolved",
                new
                {
                    incident.IncidentId,
                    incident.TripId,
                    incident.Status,
                    incident.ResolvedAt
                });

            return ApiResponse<bool>.SuccessResponse(true, "Incident resolved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve incident. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<bool>.Failure($"Failed to resolve incident: {ex.Message}");
        }
    }

    public async Task<ApiResponse<IncidentResponse>> GetIncidentByIdAsync(Guid incidentId)
    {
        try
        {
            var incident = await LoadIncidentAsync(incidentId);
            if (incident == null)
                return ApiResponse<IncidentResponse>.Failure("Incident not found.", 404);

            return ApiResponse<IncidentResponse>.SuccessResponse(
                MapToResponse(incident),
                "Incident details retrieved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve incident details. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentResponse>.Failure($"Failed to retrieve incident details: {ex.Message}");
        }
    }

    public async Task<ApiResponse<PagedResult<IncidentResponse>>> GetPagedIncidentsAsync(
        Guid? tripId,
        int pageNumber,
        int pageSize)
    {
        try
        {
            var safePageNumber = Math.Max(1, pageNumber);
            var safePageSize = Math.Clamp(pageSize, 1, 100);
            var query = _db.IncidentReports
                .Include(i => i.ReportedByNavigation)
                .Include(i => i.Trip)
                .Include(i => i.IncidentEvidences)
                .AsQueryable();

            if (tripId.HasValue)
                query = query.Where(i => i.TripId == tripId.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(i => i.ReportedAt)
                .Skip((safePageNumber - 1) * safePageSize)
                .Take(safePageSize)
                .ToListAsync();

            var responseList = items.Select(MapToResponse).ToList();
            var pagedResult = PagedResult<IncidentResponse>.Create(
                responseList,
                totalCount,
                safePageNumber,
                safePageSize);

            return ApiResponse<PagedResult<IncidentResponse>>.SuccessResponse(
                pagedResult,
                "Paged incidents retrieved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve paged incidents.");
            return ApiResponse<PagedResult<IncidentResponse>>.Failure($"Failed to retrieve incidents: {ex.Message}");
        }
    }

    public async Task<int> EscalateOverdueReportedIncidentsAsync(DateTime asOf)
    {
        var normalizedAsOf = DateTime.SpecifyKind(asOf, DateTimeKind.Unspecified);
        var repeatBefore = normalizedAsOf.AddMinutes(-_reportedSlaMinutes);
        var overdue = await _db.IncidentReports
            .Where(i => i.Status == "REPORTED"
                        && i.SlaDueAt.HasValue
                        && i.SlaDueAt.Value <= normalizedAsOf
                        && (!i.LastSlaEscalatedAt.HasValue || i.LastSlaEscalatedAt.Value <= repeatBefore))
            .OrderBy(i => i.SlaDueAt)
            .ToListAsync();
        if (overdue.Count == 0)
            return 0;

        var recipients = await _db.Users
            .Where(u => u.Role != null && IncidentRecipientRoles.Contains(u.Role.RoleName.ToUpper()))
            .Select(u => u.UserId)
            .ToListAsync();
        var templateId = await EnsureNotificationTemplateAsync(
            SlaEscalatedTemplateId,
            "Incident {{incident_id}} is awaiting triage",
            "Incident {{incident_id}} on trip {{trip_id}} has remained REPORTED beyond its handling SLA.");

        foreach (var incident in overdue)
        {
            incident.LastSlaEscalatedAt = normalizedAsOf;
            if (templateId == null)
                continue;

            var parameters = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["incident_id"] = incident.IncidentId.ToString(),
                ["trip_id"] = incident.TripId?.ToString() ?? "N/A",
                ["risk_level"] = incident.RiskLevel ?? incident.Severity,
                ["sla_due_at"] = incident.SlaDueAt?.ToString("O") ?? "N/A"
            });
            foreach (var recipient in recipients.Distinct())
            {
                _db.Notifications.Add(new Notification
                {
                    NotiId = Guid.NewGuid(),
                    UserId = recipient,
                    SenderId = incident.ReportedBy,
                    TemplateId = templateId,
                    Params = parameters,
                    IsRead = false,
                    CreatedAt = normalizedAsOf
                });
            }
        }

        await _db.SaveChangesAsync();
        foreach (var incident in overdue)
        {
            await SafeNotifyGroupsAsync(
                new[] { "Group_Dispatcher", "Group_Admin" },
                "IncidentSlaEscalated",
                new
                {
                    incident.IncidentId,
                    incident.TripId,
                    incident.RiskLevel,
                    incident.SlaDueAt,
                    incident.LastSlaEscalatedAt
                });
        }

        return overdue.Count;
    }

    private Task<IncidentReport?> LoadIncidentAsync(Guid incidentId)
    {
        return _db.IncidentReports
            .Include(i => i.ReportedByNavigation)
            .Include(i => i.Trip)
            .Include(i => i.IncidentEvidences)
            .FirstOrDefaultAsync(i => i.IncidentId == incidentId);
    }

    private async Task<string?> EnsureNotificationTemplateAsync(
        string templateId,
        string titleTemplate,
        string bodyTemplate)
    {
        var existing = await _db.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == templateId);
        if (existing != null)
        {
            existing.TitleTemplate = titleTemplate;
            existing.BodyTemplate = bodyTemplate;
            existing.Channel = "IN_APP";
            existing.Status = "ACTIVE";
            return templateId;
        }

        var typeId = await _db.Messagetypes
            .Select(m => (Guid?)m.TypeId)
            .FirstOrDefaultAsync();
        if (!typeId.HasValue)
        {
            _logger.LogWarning(
                "Cannot create incident notification template {TemplateId}: no message type exists.",
                templateId);
            return null;
        }

        _db.NotificationTemplates.Add(new NotificationTemplate
        {
            TemplateId = templateId,
            TypeId = typeId.Value,
            TitleTemplate = titleTemplate,
            BodyTemplate = bodyTemplate,
            Channel = "IN_APP",
            Status = "ACTIVE"
        });
        return templateId;
    }

    private async Task AddUserNotificationAsync(
        Guid userId,
        Guid senderId,
        string templateId,
        string titleTemplate,
        string bodyTemplate,
        Dictionary<string, string> parameters,
        DateTime createdAt)
    {
        var ensuredTemplateId = await EnsureNotificationTemplateAsync(
            templateId,
            titleTemplate,
            bodyTemplate);
        if (ensuredTemplateId == null)
            return;

        _db.Notifications.Add(new Notification
        {
            NotiId = Guid.NewGuid(),
            UserId = userId,
            SenderId = senderId,
            TemplateId = ensuredTemplateId,
            Params = JsonSerializer.Serialize(parameters),
            IsRead = false,
            CreatedAt = createdAt
        });
    }

    private async Task SafeNotifyGroupsAsync(
        IReadOnlyCollection<string> groups,
        string eventName,
        object payload)
    {
        if (_realtimeNotifier == null)
            return;

        try
        {
            await _realtimeNotifier.NotifyGroupsAsync(groups, eventName, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime incident notification {EventName} failed.", eventName);
        }
    }

    private async Task SafeNotifyUserAsync(Guid userId, string eventName, object payload)
    {
        if (_realtimeNotifier == null)
            return;

        try
        {
            await _realtimeNotifier.NotifyUserAsync(userId, eventName, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Realtime incident notification {EventName} failed for user {UserId}.",
                eventName,
                userId);
        }
    }

    private static string? ValidateEvidenceFiles(
        IReadOnlyCollection<IFormFile> files,
        bool allowEmpty = false)
    {
        if (files.Count == 0)
            return allowEmpty ? null : "At least one evidence file is required.";
        if (files.Count > MaxEvidenceFiles)
            return $"A maximum of {MaxEvidenceFiles} evidence files is allowed per request.";

        foreach (var file in files)
        {
            if (file.Length <= 0)
                return $"Evidence file '{file.FileName}' is empty.";
            if (file.Length > MaxEvidenceFileSize)
                return $"Evidence file '{file.FileName}' must be smaller than 10MB.";

            var isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            var isPdf = file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (!isImage && !isPdf)
                return $"Evidence file '{file.FileName}' must be an image or PDF.";
        }

        return null;
    }

    private static string InferEvidenceType(IFormFile file)
    {
        var name = file.FileName.ToLowerInvariant();
        return file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("receipt") ||
               name.Contains("invoice") ||
               name.Contains("hoa-don") ||
               name.Contains("hoadon")
            ? "DRIVER_RECEIPT"
            : "INCIDENT_PHOTO";
    }

    private static IncidentResponse MapToResponse(IncidentReport incident)
    {
        var description = incident.Description;
        var resolutionNote = incident.ResolutionNote;

        if (resolutionNote == null && description.Contains(" | Resolution: "))
        {
            var parts = description.Split(
                new[] { " | Resolution: " },
                StringSplitOptions.None);
            description = parts[0];
            resolutionNote = parts.ElementAtOrDefault(1);
        }

        return new IncidentResponse
        {
            IncidentId = incident.IncidentId,
            TripId = incident.TripId,
            TripCode = incident.TripId?.ToString() ?? "N/A",
            IncidentType = incident.IncidentType,
            Severity = incident.Severity,
            RiskLevel = incident.RiskLevel,
            Description = description,
            CurrentLatitude = incident.CurrentLatitude,
            CurrentLongitude = incident.CurrentLongitude,
            DriverPaidAmount = incident.DriverPaidAmount,
            RequiresRescue = incident.RequiresRescue,
            TemperatureSource = incident.TemperatureSource,
            LatestTemperature = incident.LatestTemperature,
            TemperatureMeasuredAt = incident.TemperatureMeasuredAt,
            TemperatureTolerance = incident.TemperatureTolerance,
            TemperatureThresholdBreached = incident.TemperatureThresholdBreached,
            ContainmentConfirmedAt = incident.ContainmentConfirmedAt,
            RemainingSafeTimeMinutes = incident.RemainingSafeTimeMinutes,
            SafeTimeCalculation = incident.SafeTimeCalculation,
            DirectDeliveryLocked = incident.DirectDeliveryLocked,
            PreviousIncidentId = incident.PreviousIncidentId,
            SlaDueAt = incident.SlaDueAt,
            LastSlaEscalatedAt = incident.LastSlaEscalatedAt,
            RescuePlanType = incident.RescuePlanType,
            RescuePlanDetails = incident.RescuePlanDetails,
            RedispatchPlan = incident.RedispatchPlan,
            ApprovedAmount = incident.ApprovedAmount,
            ReimbursedAmount = incident.ReimbursedAmount,
            ExpenseStatus = incident.ExpenseStatus,
            Status = incident.Status ?? "REPORTED",
            ReportedBy = incident.ReportedBy,
            ReportedByUsername = incident.ReportedByNavigation?.Username ?? "Unknown",
            ReportedAt = incident.ReportedAt,
            HandledBy = incident.HandledBy,
            HandledAt = incident.HandledAt,
            HandlingNote = incident.HandlingNote,
            BrokenVehicleId = incident.BrokenVehicleId,
            ReplacementVehicleId = incident.ReplacementVehicleId,
            MaintenanceTicketId = incident.MaintenanceTicketId,
            RescueDispatchedAt = incident.RescueDispatchedAt,
            TransloadConfirmedBy = incident.TransloadConfirmedBy,
            TransloadConfirmedAt = incident.TransloadConfirmedAt,
            TransloadNote = incident.TransloadNote,
            TransloadDetails = DeserializeTransloadRecord(incident.TransloadDetailsJson),
            ExpenseApprovedBy = incident.ExpenseApprovedBy,
            ExpenseApprovedAt = incident.ExpenseApprovedAt,
            ExpenseApprovalNote = incident.ExpenseApprovalNote,
            ReimbursedBy = incident.ReimbursedBy,
            ReimbursedAt = incident.ReimbursedAt,
            ResolvedBy = incident.ResolvedBy,
            ResolvedAt = incident.ResolvedAt,
            ResolutionNote = resolutionNote,
            Evidences = incident.IncidentEvidences.Select(e => new IncidentEvidenceResponse
            {
                EvidenceId = e.EvidenceId,
                EvidenceType = e.EvidenceType,
                FileUrl = e.FileUrl
            }).ToList()
        };
    }

    private static IncidentRiskLevel MapLegacySeverityToRisk(IncidentSeverity? severity)
        => severity switch
        {
            IncidentSeverity.LOW => IncidentRiskLevel.LOW,
            IncidentSeverity.MEDIUM => IncidentRiskLevel.WARNING,
            IncidentSeverity.HIGH => IncidentRiskLevel.CRITICAL,
            IncidentSeverity.CRITICAL => IncidentRiskLevel.CRITICAL,
            _ => IncidentRiskLevel.WARNING
        };

    private static IncidentSeverity MapRiskToLegacySeverity(IncidentRiskLevel riskLevel)
        => riskLevel switch
        {
            IncidentRiskLevel.LOW => IncidentSeverity.LOW,
            IncidentRiskLevel.WARNING => IncidentSeverity.MEDIUM,
            _ => IncidentSeverity.CRITICAL
        };

    private static (int? Minutes, string Method) CalculateRemainingSafeTime(
        decimal targetTemperature,
        decimal tolerance,
        decimal? measuredTemperature,
        DateTime? measuredAt,
        IReadOnlyList<TelemetryLog> telemetry,
        bool thresholdBreached)
    {
        if (thresholdBreached)
            return (0, "THRESHOLD_ALREADY_BREACHED");
        if (!measuredTemperature.HasValue)
            return (null, "NO_TRUSTED_TEMPERATURE");

        var samples = telemetry
            .Where(t => t.Timestamp <= (measuredAt ?? DateTime.MaxValue))
            .OrderBy(t => t.Timestamp)
            .TakeLast(12)
            .ToList();
        if (samples.Count < 2)
            return (null, "INSUFFICIENT_TREND_DATA");

        var first = samples[0];
        var last = samples[^1];
        var elapsedMinutes = (decimal)(last.Timestamp - first.Timestamp).TotalMinutes;
        if (elapsedMinutes <= 0)
            return (null, "INSUFFICIENT_TREND_DATA");

        var slope = (last.Temperature - first.Temperature) / elapsedMinutes;
        var upperBound = targetTemperature + tolerance;
        var lowerBound = targetTemperature - tolerance;
        decimal minutesToBoundary;
        if (slope > 0.001m)
            minutesToBoundary = (upperBound - measuredTemperature.Value) / slope;
        else if (slope < -0.001m)
            minutesToBoundary = (measuredTemperature.Value - lowerBound) / -slope;
        else
            return (null, "STABLE_TREND_NO_PREDICTED_BREACH");

        if (minutesToBoundary <= 0)
            return (0, "LINEAR_TELEMETRY_TREND");

        return ((int)Math.Ceiling(Math.Min(minutesToBoundary, 24m * 60m)), "LINEAR_TELEMETRY_TREND");
    }

    private static TransloadRecord? DeserializeTransloadRecord(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TransloadRecord>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValidEvidenceUrl(string url)
        => !string.IsNullOrWhiteSpace(url)
           && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
           && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    private static string FormatLocation(decimal? latitude, decimal? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
            return "N/A";

        return $"{latitude.Value:0.#######}, {longitude.Value:0.#######}";
    }

    private static string FormatDateTime(DateTime? value)
        => value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm:ss") : "N/A";

    private async Task<(decimal? Latitude, decimal? Longitude)> ResolveIncidentVehicleLocationAsync(
        MasterTrip? trip,
        decimal? fallbackLatitude,
        decimal? fallbackLongitude)
    {
        if (trip == null)
            return (fallbackLatitude, fallbackLongitude);

        if (_realtimeTelemetryService != null && trip.Vehicle?.IotDevices != null)
        {
            foreach (var deviceCode in trip.Vehicle.IotDevices
                         .Select(d => d.DeviceCode)
                         .Where(code => !string.IsNullOrWhiteSpace(code))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var realtimeGps = await _realtimeTelemetryService.GetLatestGpsPositionAsync(deviceCode!);
                    if (realtimeGps != null && HasUsableCoordinates(realtimeGps.Latitude, realtimeGps.Longitude))
                        return (realtimeGps.Latitude, realtimeGps.Longitude);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve realtime GPS for incident trip {TripId}, device {DeviceCode}.", trip.TripId, deviceCode);
                }
            }
        }

        var latestTelemetry = await _db.TelemetryLogs
            .AsNoTracking()
            .Where(t => t.TripId == trip.TripId)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync();

        if (latestTelemetry != null && HasUsableCoordinates(latestTelemetry.Latitude, latestTelemetry.Longitude))
            return (latestTelemetry.Latitude, latestTelemetry.Longitude);

        return (fallbackLatitude, fallbackLongitude);
    }

    private static bool HasUsableCoordinates(decimal latitude, decimal longitude)
        => (latitude != 0m || longitude != 0m)
           && latitude is >= -90m and <= 90m
           && longitude is >= -180m and <= 180m;

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
