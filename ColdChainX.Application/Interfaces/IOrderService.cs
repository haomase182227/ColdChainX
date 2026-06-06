using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IOrderService
    {
        Task<ApiResponse<IReadOnlyCollection<OrderResponse>>> GetOrdersAsync();
        Task<ApiResponse<OrderResponse>> GetOrderByIdAsync(Guid orderId);
        Task<ApiResponse<IReadOnlyCollection<OrderResponse>>> GetOrdersByCustomerAsync(Guid customerId);
        Task<ApiResponse<CreateOrderResponse>> CreateOrderAsync(CreateOrderRequest request);
        Task<ApiResponse<ReviewOrderResponse>> ReviewOrderAsync(Guid orderId, ReviewOrderRequest request);
    }
}
