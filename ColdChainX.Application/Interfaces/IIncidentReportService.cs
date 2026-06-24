using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IIncidentReportService
    {
        Task<ApiResponse<IncidentResponse>> ReportIncidentAsync(CreateIncidentRequest request, Guid userId);
        Task<ApiResponse<bool>> ResolveIncidentAsync(Guid incidentId, string resolutionNote, Guid userId);
        Task<ApiResponse<IncidentResponse>> GetIncidentByIdAsync(Guid incidentId);
        Task<ApiResponse<PagedResult<IncidentResponse>>> GetPagedIncidentsAsync(Guid? tripId, int pageNumber, int pageSize);
    }
}
