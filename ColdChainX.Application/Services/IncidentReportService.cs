using System.Globalization;
using System.Text.Json;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Services;

public class IncidentReportService : IIncidentReportService
{
    private const string ReportedTemplateId = "INCIDENT_REPORTED";
    private const string ExpenseApprovedTemplateId = "INCIDENT_EXPENSE_APPROVED";
    private const string ReimbursedTemplateId = "INCIDENT_REIMBURSED";
    private const string ResolvedTemplateId = "INCIDENT_RESOLVED";
    private const int MaxEvidenceFiles = 5;
    private const long MaxEvidenceFileSize = 10 * 1024 * 1024;

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

    public IncidentReportService(
        IApplicationDbContext db,
        IPdfGeneratorService pdfGeneratorService,
        IFileService fileService,
        ILogger<IncidentReportService> logger,
        IIncidentRealtimeNotifier? realtimeNotifier = null)
    {
        _db = db;
        _pdfGeneratorService = pdfGeneratorService;
        _fileService = fileService;
        _logger = logger;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<ApiResponse<IncidentResponse>> ReportIncidentAsync(
        CreateIncidentRequest request,
        Guid userId,
        IReadOnlyCollection<IFormFile>? evidenceFiles = null)
    {
        if (request == null)
            return ApiResponse<IncidentResponse>.Failure("Request is null.");
        if (!request.IncidentType.HasValue)
            return ApiResponse<IncidentResponse>.Failure("Incident type is required.");
        if (!request.Severity.HasValue)
            return ApiResponse<IncidentResponse>.Failure("Severity is required.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return ApiResponse<IncidentResponse>.Failure("Description is required.");
        if (request.DriverPaidAmount < 0)
            return ApiResponse<IncidentResponse>.Failure("Driver-paid amount cannot be negative.");
        if (request.CurrentLatitude is < -90m or > 90m)
            return ApiResponse<IncidentResponse>.Failure("Current latitude must be between -90 and 90.");
        if (request.CurrentLongitude is < -180m or > 180m)
            return ApiResponse<IncidentResponse>.Failure("Current longitude must be between -180 and 180.");

        var files = evidenceFiles?.Where(f => f != null).ToList() ?? new List<IFormFile>();
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
                    .FirstOrDefaultAsync(t => t.TripId == request.TripId.Value);
                if (trip == null)
                    return ApiResponse<IncidentResponse>.Failure("Trip not found.");
            }

            var isDriver = string.Equals(
                               reporter.Role?.RoleName,
                               "Driver",
                               StringComparison.OrdinalIgnoreCase) ||
                           await _db.Drivers.AnyAsync(d => d.UserId == userId);
            if (isDriver)
            {
                if (!request.TripId.HasValue)
                    return ApiResponse<IncidentResponse>.Failure("Driver incident reports must be linked to a trip.");
                if (!request.CurrentLatitude.HasValue || !request.CurrentLongitude.HasValue)
                    return ApiResponse<IncidentResponse>.Failure("Current location is required for driver incident reports.");

                var assignedToTrip = await _db.TripDrivers.AnyAsync(td =>
                    td.TripId == request.TripId.Value &&
                    td.Driver.UserId == userId);
                if (!assignedToTrip)
                    return ApiResponse<IncidentResponse>.Failure("Driver is not assigned to this trip.", 403);
            }

            var uploadedEvidences = new List<(string Type, string Url)>();
            foreach (var file in files)
            {
                var url = await _fileService.UploadFileAsync(file);
                uploadedEvidences.Add((InferEvidenceType(file), url));
            }

            var now = DbNow();
            var incident = new IncidentReport
            {
                IncidentId = Guid.NewGuid(),
                TripId = request.TripId,
                IncidentType = request.IncidentType.Value.ToString(),
                Severity = request.Severity.Value.ToString(),
                Description = request.Description.Trim(),
                CurrentLatitude = request.CurrentLatitude,
                CurrentLongitude = request.CurrentLongitude,
                DriverPaidAmount = request.DriverPaidAmount,
                RequiresRescue = request.RequiresRescue,
                ExpenseStatus = request.DriverPaidAmount > 0 ? "PENDING_APPROVAL" : "NOT_REQUIRED",
                Status = "REPORTED",
                ReportedBy = userId,
                ReportedAt = now
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

            var templateId = await EnsureNotificationTemplateAsync(
                ReportedTemplateId,
                "Sự cố mới trên chuyến {{trip_id}}",
                "Tài xế {{reporter_name}} vừa báo sự cố {{incident_type}} mức {{severity}}. Yêu cầu cứu hộ: {{requires_rescue}}.");

            if (templateId != null)
            {
                var recipientIds = await _db.Users
                    .Where(u => u.Role != null &&
                                IncidentRecipientRoles.Contains(u.Role.RoleName.ToUpper()))
                    .Select(u => u.UserId)
                    .ToListAsync();

                var parameters = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["incident_id"] = incident.IncidentId.ToString(),
                    ["trip_id"] = incident.TripId?.ToString() ?? "N/A",
                    ["reporter_name"] = reporter.FullName,
                    ["incident_type"] = incident.IncidentType,
                    ["severity"] = incident.Severity,
                    ["requires_rescue"] = incident.RequiresRescue ? "Có" : "Không"
                });

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

            await _db.SaveChangesAsync();

            await SafeNotifyGroupsAsync(
                new[] { "Group_Dispatcher", "Group_Admin" },
                "IncidentReported",
                new
                {
                    incident.IncidentId,
                    incident.TripId,
                    incident.IncidentType,
                    incident.Severity,
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
            var isPrivileged = actor?.Role?.RoleName is not null &&
                               (actor.Role.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                                actor.Role.RoleName.Equals("Manager", StringComparison.OrdinalIgnoreCase));
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

            await _db.SaveChangesAsync();

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
                var requiredOperationalStatus = incident.RequiresRescue
                    ? "TRANSLOAD_COMPLETED"
                    : "CONTINUED";
                if (incident.Status != requiredOperationalStatus)
                {
                    return ApiResponse<bool>.Failure(
                        $"Incident can only be resolved after operational status {requiredOperationalStatus}.");
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
                incident.Description,
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

        // Backward compatibility for incidents resolved before ResolutionNote had
        // its own database column.
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
            Description = description,
            CurrentLatitude = incident.CurrentLatitude,
            CurrentLongitude = incident.CurrentLongitude,
            DriverPaidAmount = incident.DriverPaidAmount,
            RequiresRescue = incident.RequiresRescue,
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

    private static string FormatLocation(decimal? latitude, decimal? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
            return "N/A";

        return $"{latitude.Value:0.#######}, {longitude.Value:0.#######}";
    }

    private static string FormatDateTime(DateTime? value)
        => value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm:ss") : "N/A";

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
