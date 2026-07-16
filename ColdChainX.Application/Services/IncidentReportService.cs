using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class IncidentReportService : IIncidentReportService
    {
        private readonly IApplicationDbContext _db;
        private readonly IPdfGeneratorService _pdfGeneratorService;
        private readonly IFileService _fileService;
        private readonly ILogger<IncidentReportService> _logger;

        public IncidentReportService(
            IApplicationDbContext db,
            IPdfGeneratorService pdfGeneratorService,
            IFileService fileService,
            ILogger<IncidentReportService> logger)
        {
            _db = db;
            _pdfGeneratorService = pdfGeneratorService;
            _fileService = fileService;
            _logger = logger;
        }

        public async Task<ApiResponse<IncidentResponse>> ReportIncidentAsync(CreateIncidentRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<IncidentResponse>.Failure("Request is null");

            if (!request.IncidentType.HasValue)
                return ApiResponse<IncidentResponse>.Failure("Incident type is required.");

            if (!request.Severity.HasValue)
                return ApiResponse<IncidentResponse>.Failure("Severity is required.");

            if (string.IsNullOrWhiteSpace(request.Description))
                return ApiResponse<IncidentResponse>.Failure("Description is required.");

            if (request.DriverPaidAmount < 0)
                return ApiResponse<IncidentResponse>.Failure("Driver-paid amount cannot be negative.");

            try
            {
                var reporter = await _db.Users.FindAsync(userId);
                if (reporter == null)
                    return ApiResponse<IncidentResponse>.Failure("Reporter user not found.");

                if (request.TripId.HasValue)
                {
                    var tripExists = await _db.MasterTrips.AnyAsync(t => t.TripId == request.TripId.Value);
                    if (!tripExists)
                        return ApiResponse<IncidentResponse>.Failure("Trip not found.");
                }

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
                    Status = "REPORTED",
                    ReportedBy = userId,
                    ReportedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };

                _db.IncidentReports.Add(incident);
                await _db.SaveChangesAsync();

                // Reload navigation properties
                var savedIncident = await _db.IncidentReports
                    .Include(i => i.ReportedByNavigation)
                    .Include(i => i.Trip)
                    .Include(i => i.IncidentEvidences)
                    .FirstOrDefaultAsync(i => i.IncidentId == incident.IncidentId);

                var response = MapToResponse(savedIncident!);
                return ApiResponse<IncidentResponse>.SuccessResponse(response, "Incident reported successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to report incident. TripId: {TripId}", request.TripId);
                return ApiResponse<IncidentResponse>.Failure($"Failed to report incident: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> ResolveIncidentAsync(Guid incidentId, ResolveIncidentRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<bool>.Failure("Request is null.");

            if (string.IsNullOrWhiteSpace(request.ResolutionNote))
                return ApiResponse<bool>.Failure("Resolution note is required.");

            if (request.ReimbursedAmount < 0)
                return ApiResponse<bool>.Failure("Reimbursed amount cannot be negative.");

            try
            {
                var incident = await _db.IncidentReports
                    .Include(i => i.ReportedByNavigation)
                    .Include(i => i.Trip)
                    .Include(i => i.IncidentEvidences)
                    .FirstOrDefaultAsync(i => i.IncidentId == incidentId);

                if (incident == null)
                    return ApiResponse<bool>.Failure("Incident not found.");

                if (incident.Status == "RESOLVED")
                    return ApiResponse<bool>.Failure("Incident is already resolved.");

                var resolvedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                var resolutionNote = request.ResolutionNote.Trim();
                var resolver = await _db.Users.FindAsync(userId);
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
                    ReimbursedAmount = request.ReimbursedAmount.ToString("N2", viCulture),
                    ReporterName = incident.ReportedByNavigation?.FullName
                        ?? incident.ReportedByNavigation?.Username
                        ?? incident.ReportedBy.ToString(),
                    ResolverName = resolver?.FullName ?? resolver?.Username ?? userId.ToString(),
                    ReportedAt = FormatDateTime(incident.ReportedAt),
                    ResolvedAt = FormatDateTime(resolvedAt)
                };

                var pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("IncidentResolution", documentData);
                var fileUrl = await _fileService.UploadFileAsync(
                    pdfBytes,
                    $"incident_resolution_{incident.IncidentId:N}.pdf");

                incident.Status = "RESOLVED";
                incident.Description = $"{incident.Description} | Resolution: {resolutionNote}";
                incident.ReimbursedAmount = request.ReimbursedAmount;
                incident.ResolvedAt = resolvedAt;

                var evidence = new IncidentEvidence
                {
                    EvidenceId = Guid.NewGuid(),
                    IncidentId = incident.IncidentId,
                    EvidenceType = "RESOLUTION_PDF",
                    FileUrl = fileUrl,
                    Incident = incident
                };

                _db.IncidentEvidences.Add(evidence);

                await _db.SaveChangesAsync();
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
                var incident = await _db.IncidentReports
                    .Include(i => i.ReportedByNavigation)
                    .Include(i => i.Trip)
                    .Include(i => i.IncidentEvidences)
                    .FirstOrDefaultAsync(i => i.IncidentId == incidentId);

                if (incident == null)
                    return ApiResponse<IncidentResponse>.Failure("Incident not found.");

                var response = MapToResponse(incident);
                return ApiResponse<IncidentResponse>.SuccessResponse(response, "Incident details retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve incident details. IncidentId: {IncidentId}", incidentId);
                return ApiResponse<IncidentResponse>.Failure($"Failed to retrieve incident details: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PagedResult<IncidentResponse>>> GetPagedIncidentsAsync(Guid? tripId, int pageNumber, int pageSize)
        {
            try
            {
                var query = _db.IncidentReports
                    .Include(i => i.ReportedByNavigation)
                    .Include(i => i.Trip)
                    .Include(i => i.IncidentEvidences)
                    .AsQueryable();

                if (tripId.HasValue)
                {
                    query = query.Where(i => i.TripId == tripId.Value);
                }

                int totalCount = await query.CountAsync();
                var items = await query
                    .OrderByDescending(i => i.ReportedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var responseList = items.Select(MapToResponse).ToList();
                var pagedResult = PagedResult<IncidentResponse>.Create(responseList, totalCount, pageNumber, pageSize);

                return ApiResponse<PagedResult<IncidentResponse>>.SuccessResponse(pagedResult, "Paged incidents retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve paged incidents.");
                return ApiResponse<PagedResult<IncidentResponse>>.Failure($"Failed to retrieve incidents: {ex.Message}");
            }
        }

        private static IncidentResponse MapToResponse(IncidentReport incident)
        {
            var description = incident.Description;
            string? resolutionNote = null;
            if (description.Contains(" | Resolution: "))
            {
                var parts = description.Split(new[] { " | Resolution: " }, StringSplitOptions.None);
                description = parts[0];
                resolutionNote = parts[1];
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
                ReimbursedAmount = incident.ReimbursedAmount,
                Status = incident.Status ?? "REPORTED",
                ReportedBy = incident.ReportedBy,
                ReportedByUsername = incident.ReportedByNavigation?.Username ?? "Unknown",
                ReportedAt = incident.ReportedAt,
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
    }
}
