using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Interfaces
{
    public interface ICycleCountRepository
    {
        Task AddPlanAsync(CycleCountPlan plan);
        Task<CycleCountPlan?> GetPlanByIdAsync(Guid planId);
        Task<CycleCountPlan?> GetPlanByCodeAsync(string planCode);
        Task<List<CycleCountPlan>> GetPagedPlansAsync(int pageNumber, int pageSize, CycleCountPlanStatus? status, Guid? warehouseId);
        Task<int> CountPlansAsync(CycleCountPlanStatus? status, Guid? warehouseId);
        Task<CycleCountEntry?> GetEntryByIdAsync(Guid entryId);
        Task UpdatePlanAsync(CycleCountPlan plan);
        Task UpdateEntryAsync(CycleCountEntry entry);
        Task SaveChangesAsync();
    }
}
