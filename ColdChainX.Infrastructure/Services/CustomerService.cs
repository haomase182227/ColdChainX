using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Customers;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services
{
    public class CustomerService : ICustomerService
    {
        private readonly ApplicationDbContext _db;

        public CustomerService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<PagedResult<CustomerResponse>>> GetCustomersAsync(int pageNumber, int pageSize)
        {
            var query = _db.Customers
                .AsNoTracking()
                .Include(c => c.TransportOrders)
                .Include(c => c.CustomerContracts)
                .OrderByDescending(c => c.CreatedAt);
            var totalRecords = await query.CountAsync();
            var data = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(c => ToCustomerResponse(c))
                .ToListAsync();

            return ApiResponse<PagedResult<CustomerResponse>>.SuccessResponse(
                PagedResult<CustomerResponse>.Create(data, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Customers retrieved successfully");
        }

        public async Task<ApiResponse<CustomerResponse>> GetCustomerByIdAsync(Guid customerId)
        {
            var customer = await _db.Customers
                .AsNoTracking()
                .Include(c => c.TransportOrders)
                .Include(c => c.CustomerContracts)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (customer == null)
                return ApiResponse<CustomerResponse>.Failure("Customer not found");

            return ApiResponse<CustomerResponse>.SuccessResponse(ToCustomerResponse(customer), "Customer retrieved successfully");
        }

        private static CustomerResponse ToCustomerResponse(Customer customer)
        {
            return new CustomerResponse
            {
                CustomerId = customer.CustomerId,
                CompanyName = customer.CompanyName,
                TaxCode = customer.TaxCode,
                Address = customer.Address,
                Email = customer.Email,
                PaymentTerm = customer.PaymentTerm,
                Status = customer.Status,
                CreatedAt = customer.CreatedAt,
                OrderCount = customer.TransportOrders.Count,
                ContractCount = customer.CustomerContracts.Count
            };
        }

        private static int NormalizePageSize(int pageSize)
            => Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);

        private static int NormalizeSkip(int pageNumber, int pageSize)
        {
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;
            return (safePageNumber - 1) * NormalizePageSize(pageSize);
        }
    }
}
