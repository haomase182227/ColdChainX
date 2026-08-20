using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IIncidentRescueService
    {
        Task<ApiResponse<List<RescueCandidateResponse>>> GetRescueCandidatesAsync(Guid incidentId);

        Task<ApiResponse<IncidentRescuePlanResponse>> GetRescuePlanAsync(Guid incidentId);

        Task<ApiResponse<RescueFallbackResult>> RecordFallbackAsync(
            Guid incidentId,
            RecordRescueFallbackRequest request,
            Guid dispatcherId);

        Task<ApiResponse<ExternalReeferWorkflowResult>> DispatchExternalReeferAsync(
            Guid incidentId,
            DispatchExternalReeferRequest request,
            Guid dispatcherId);

        Task<ApiResponse<ExternalReeferWorkflowResult>> InboundRouteWarehouseAsync(
            Guid incidentId,
            InboundRouteWarehouseRequest request,
            Guid confirmedBy);

        Task<ApiResponse<IncidentWorkflowResult>> ContinueTripAsync(
            Guid incidentId,
            ContinueTripAfterIncidentRequest request,
            Guid driverUserId);

        Task<ApiResponse<IncidentRescueResult>> DispatchRescueAsync(Guid incidentId, DispatchRescueRequest request, Guid dispatcherId);

        Task<ApiResponse<IncidentWorkflowResult>> ConfirmTransloadAsync(
            Guid incidentId,
            ConfirmTransloadRequest request,
            Guid confirmedBy);
    }
}
