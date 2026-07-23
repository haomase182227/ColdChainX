using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IIncidentReportService
    {
        Task<ApiResponse<IncidentResponse>> ReportIncidentAsync(
            CreateIncidentRequest request,
            Guid userId,
            IReadOnlyCollection<IFormFile>? evidenceFiles = null);
        Task<ApiResponse<IncidentResponse>> AddEvidenceAsync(
            Guid incidentId,
            IReadOnlyCollection<IFormFile> files,
            string evidenceType,
            Guid userId);
        Task<ApiResponse<IncidentResponse>> ApproveExpenseAsync(
            Guid incidentId,
            ApproveIncidentExpenseRequest request,
            Guid adminId);
        Task<ApiResponse<IncidentResponse>> ReimburseExpenseAsync(
            Guid incidentId,
            ReimburseIncidentExpenseRequest request,
            Guid adminId);
        Task<ApiResponse<bool>> ResolveIncidentAsync(Guid incidentId, ResolveIncidentRequest request, Guid userId);
        Task<ApiResponse<IncidentResponse>> GetIncidentByIdAsync(Guid incidentId);
        Task<ApiResponse<PagedResult<IncidentResponse>>> GetPagedIncidentsAsync(Guid? tripId, int pageNumber, int pageSize);
    }
}
