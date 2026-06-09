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

        public async Task<ApiResponse<IReadOnlyCollection<CustomerResponse>>> GetCustomersAsync()
        {
            var data = await _db.Customers
                .AsNoTracking()
                .Include(c => c.TransportOrders)
                .Include(c => c.CustomerContracts)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => ToCustomerResponse(c))
                .ToListAsync();

            return ApiResponse<IReadOnlyCollection<CustomerResponse>>.SuccessResponse(data, "Customers retrieved successfully");
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
    }
}
