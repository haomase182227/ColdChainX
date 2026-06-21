using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces;

public interface IWarehouseFlowService
{
    Task<ApiResponse<LpnResponse>> ProcessInboundQcAsync(Guid orderId, ProcessInboundQcRequest request, Guid receiverId);

    Task<ApiResponse<object>> ResolveDiscrepancyAsync(Guid lpnId, ResolveDiscrepancyRequest request, Guid salesUserId);

    Task<ApiResponse<LpnResponse>> PutawayLpnAsync(Guid lpnId, PutawayLpnRequest request);

    Task<ApiResponse<List<LpnResponse>>> GetInventoryAgingAsync(string? state, string? storageLocation);

    Task<ApiResponse<LpnResponse>> PickLpnAsync(Guid lpnId);

    Task<ApiResponse<TripLoadingResponse>> CompleteTripLoadingAsync(Guid tripId, CompleteTripLoadingRequest request);

    Task<ApiResponse<PenaltyBillResponse>> MarkPenaltyBillPaidAsync(Guid penaltyBillId, Guid accountantUserId);

    Task<ApiResponse<LpnResponse>> GenerateReturnPdfAsync(Guid lpnId);
}
