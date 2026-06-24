using System;
using System.Collections.Generic;
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
        private readonly ILogger<IncidentReportService> _logger;

        public IncidentReportService(IApplicationDbContext db, ILogger<IncidentReportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse<IncidentResponse>> ReportIncidentAsync(CreateIncidentRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<IncidentResponse>.Failure("Request is null");

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
                    IncidentType = request.IncidentType.Trim().ToUpperInvariant(),
                    Severity = request.Severity.Trim().ToUpperInvariant(),
                    Description = request.Description.Trim(),
                    CurrentLatitude = request.CurrentLatitude,
                    CurrentLongitude = request.CurrentLongitude,
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

        public async Task<ApiResponse<bool>> ResolveIncidentAsync(Guid incidentId, string resolutionNote, Guid userId)
        {
            if (string.IsNullOrWhiteSpace(resolutionNote))
                return ApiResponse<bool>.Failure("Resolution note is required.");

            try
            {
                var incident = await _db.IncidentReports.FindAsync(incidentId);
                if (incident == null)
                    return ApiResponse<bool>.Failure("Incident not found.");

                if (incident.Status == "RESOLVED")
                    return ApiResponse<bool>.Failure("Incident is already resolved.");

                incident.Status = "RESOLVED";
                incident.Description = $"{incident.Description} | Resolution: {resolutionNote.Trim()}";
                incident.ResolvedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

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
                Status = incident.Status ?? "REPORTED",
                ReportedBy = incident.ReportedBy,
                ReportedByUsername = incident.ReportedByNavigation?.Username ?? "Unknown",
                ReportedAt = incident.ReportedAt,
                ResolvedAt = incident.ResolvedAt,
                ResolutionNote = resolutionNote
            };
        }
    }
}
