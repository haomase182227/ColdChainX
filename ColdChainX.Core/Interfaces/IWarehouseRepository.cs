using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Core.Interfaces
{
    public interface IWarehouseRepository
    {
        Task<Warehouse?> GetByIdAsync(Guid warehouseId, bool includeDeleted = false);
        Task<Warehouse?> GetByCodeAsync(string warehouseCode, bool includeDeleted = false);
        Task<bool> ExistsByCodeAsync(string warehouseCode, Guid? excludeWarehouseId = null);
        Task<(IReadOnlyCollection<Warehouse> Data, int TotalCount)> GetListAsync(int pageNumber, int pageSize, string? search = null);
        Task AddAsync(Warehouse warehouse);
        Task UpdateAsync(Warehouse warehouse);
        Task SaveChangesAsync();
        Task<(IReadOnlyCollection<Lpn> Data, int TotalCount)> GetLpnsInWarehouseAsync(Guid warehouseId, int page, int pageSize);
    }
}
