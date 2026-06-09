using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Customers;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface ICustomerService
    {
        Task<ApiResponse<PagedResult<CustomerResponse>>> GetCustomersAsync(int pageNumber, int pageSize);
        Task<ApiResponse<CustomerResponse>> GetCustomerByIdAsync(Guid customerId);
    }
}
