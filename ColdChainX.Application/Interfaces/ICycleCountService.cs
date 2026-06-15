using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.CycleCount;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Interfaces
{
    public interface ICycleCountService
    {
        Task<ApiResponse<CycleCountPlanResponse>> CreatePlanAsync(CreateCycleCountPlanDto dto, Guid userId);
        Task<ApiResponse<bool>> StartCountingAsync(Guid planId, Guid userId);
        Task<ApiResponse<bool>> SubmitCountsAsync(Guid planId, SubmitCycleCountsDto dto, Guid userId);
        Task<ApiResponse<bool>> ReviewVarianceAsync(Guid entryId, ReviewVarianceDto dto, Guid managerId);
        Task<ApiResponse<CycleCountPlanResponse>> GetPlanDetailsAsync(Guid planId, Guid userId);
        Task<ApiResponse<PagedResult<CycleCountPlanResponse>>> GetPagedPlansAsync(int pageNumber, int pageSize, CycleCountPlanStatus? status, Guid? warehouseId);
    }
}
