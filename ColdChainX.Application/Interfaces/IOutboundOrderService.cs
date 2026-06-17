using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Outbound;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IOutboundOrderService
    {
        Task<ApiResponse<OutboundOrderResponse>> CreateAsync(CreateOutboundOrderRequest request, Guid currentUserId);
        Task<ApiResponse<PagedResult<OutboundOrderResponse>>> GetListAsync(int pageNumber, int pageSize, string? search = null, string? status = null, Guid? customerId = null);
        Task<ApiResponse<OutboundOrderResponse>> GetByIdAsync(Guid outboundOrderId);
        Task<ApiResponse<OutboundOrderResponse>> UpdateAsync(Guid outboundOrderId, UpdateOutboundOrderRequest request, Guid currentUserId);
        Task<ApiResponse<OutboundOrderResponse>> AllocateOrderAsync(Guid outboundOrderId, Guid userId);
        Task<ApiResponse<bool>> CancelOrderAsync(Guid outboundOrderId, Guid userId);
        Task<ApiResponse<OutboundOrderResponse>> StartPickingAsync(Guid outboundOrderId, Guid pickerId, Guid userId);
        Task<ApiResponse<OutboundOrderResponse>> CompletePickingAsync(Guid outboundOrderId, Guid userId);
        Task<ApiResponse<OutboundOrderResponse>> ShipOrderAsync(Guid outboundOrderId, Guid userId);
        Task<ApiResponse<AllocationResponse>> GetAllocationsAsync(Guid outboundOrderId);
        Task<ApiResponse<PickingListResponse>> GetPickingListAsync(Guid outboundOrderId);
    }
}
