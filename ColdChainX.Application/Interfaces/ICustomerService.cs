using ColdChainX.Application.DTOs.Customers;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface ICustomerService
    {
        Task<ApiResponse<IReadOnlyCollection<CustomerResponse>>> GetCustomersAsync();
        Task<ApiResponse<CustomerResponse>> GetCustomerByIdAsync(Guid customerId);
    }
}
