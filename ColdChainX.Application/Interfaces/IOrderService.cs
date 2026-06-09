using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IOrderService
    {
        Task<ApiResponse<PagedResult<OrderResponse>>> GetOrdersAsync(int pageNumber, int pageSize);
        Task<ApiResponse<OrderResponse>> GetOrderByIdAsync(Guid orderId);
        Task<ApiResponse<PagedResult<OrderResponse>>> GetOrdersByCustomerAsync(Guid customerId, int pageNumber, int pageSize);
        Task<ApiResponse<CreateOrderResponse>> CreateOrderAsync(CreateOrderRequest request, Guid customerId);
        Task<ApiResponse<ReviewOrderResponse>> ReviewOrderAsync(Guid orderId, ReviewOrderRequest request, Guid salesUserId);
    }
}
