using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Core.Interfaces
{
    public interface IInventoryHoldRepository
    {
        Task AddAsync(InventoryHold hold);
        Task UpdateAsync(InventoryHold hold);
        Task<InventoryHold?> GetByIdAsync(Guid holdId);
        Task<List<InventoryHold>> GetPagedHoldsAsync(int pageNumber, int pageSize, string? status, string? reasonCode, string? itemCode);
        Task<int> CountHoldsAsync(string? status, string? reasonCode, string? itemCode);
        Task<List<InventoryHold>> GetActiveHoldsByStockIdAsync(Guid stockId);
        Task SaveChangesAsync();
    }
}
