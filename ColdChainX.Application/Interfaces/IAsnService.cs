using ColdChainX.Application.DTOs.Asns;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using System;
using System.Threading.Tasks;

namespace ColdChainX.Application.Interfaces
{
    public interface IAsnService
    {
        Task<ApiResponse<AsnResponse>> CreateAsnAsync(CreateAsnRequest request, Guid customerId);

        Task<ApiResponse<PagedResult<InboundScheduleResponse>>> GetInboundSchedulesAsync(
            Guid? customerId,
            string? status,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? searchQuery,
            Guid? warehouseId,
            Guid? orderId,
            int pageNumber,
            int pageSize);

        Task<ApiResponse<List<AsnScheduleResponse>>> GetScheduleAsync(DateOnly date, string? status);

        Task<ApiResponse<List<AsnResponse>>> GetAsnsByCustomerIdAsync(Guid customerId);
    }
}
