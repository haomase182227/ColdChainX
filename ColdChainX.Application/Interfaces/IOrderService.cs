using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IOrderService
    {
        Task<ApiResponse<PagedResult<OrderResponse>>> GetOrdersAsync(int pageNumber, int pageSize, string? status = null);
        Task<ApiResponse<OrderResponse>> GetOrderByIdAsync(Guid orderId);
        Task<ApiResponse<PagedResult<CustomerOrderSummaryResponse>>> GetOrdersByCustomerAsync(Guid customerId, int pageNumber, int pageSize, string? status = null);
        Task<ApiResponse<CreateOrderResponse>> CreateOrderAsync(CreateOrderRequest request, Guid customerId);
        Task<ApiResponse<CreateOrderResponse>> UpdateOrderAsync(Guid orderId, UpdateOrderRequest request, Guid customerId);
        Task<ApiResponse<CreateOrderResponse>> AdminUpdateOrderAsync(Guid orderId, UpdateOrderRequest request, Guid salesUserId);
        Task<ApiResponse<bool>> DeleteOrderAsync(Guid orderId, Guid customerId);
        Task<ApiResponse<ReviewOrderResponse>> ReviewOrderAsync(Guid orderId, ReviewOrderRequest request, Guid salesUserId);
    }
}
