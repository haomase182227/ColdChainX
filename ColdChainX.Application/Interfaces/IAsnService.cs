using ColdChainX.Application.DTOs.Asns;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IAsnService
    {
        Task<ApiResponse<AsnResponse>> CreateAsnAsync(CreateAsnRequest request, Guid customerId);

        Task<ApiResponse<List<AsnScheduleResponse>>> GetScheduleAsync(DateOnly date, string? status);
    }
}
