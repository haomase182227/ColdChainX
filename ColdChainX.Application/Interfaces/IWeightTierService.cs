using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Routes;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IWeightTierService
    {
        Task<ApiResponse<PagedResult<WeightTierDto>>> GetAllAsync(int pageNumber, int pageSize);
        Task<ApiResponse<List<WeightTierDto>>> GetByRouteIdAsync(Guid routeId);
        Task<ApiResponse<WeightTierDto>> GetByIdAsync(Guid id);
        Task<ApiResponse<WeightTierDto>> CreateAsync(CreateUpdateWeightTierRequest request);
        Task<ApiResponse<WeightTierDto>> UpdateAsync(Guid id, CreateUpdateWeightTierRequest request);
        Task<ApiResponse<bool>> DeleteAsync(Guid id);
        Task<ApiResponse<ImportResultDto>> ImportFromCsvAsync(System.IO.Stream fileStream);
    }
}
