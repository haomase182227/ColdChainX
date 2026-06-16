using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Core.Interfaces
{
    public interface IWarehouseLocationRepository
    {
        Task<WarehouseLocation?> GetByIdAsync(Guid locationId, bool includeDeleted = false);
        Task<bool> ExistsByCodeAsync(Guid zoneId, string locationCode, Guid? excludeLocationId = null);
        Task<(IReadOnlyCollection<WarehouseLocation> Data, int TotalCount)> GetListAsync(Guid zoneId, int pageNumber, int pageSize, string? search = null);
        Task AddAsync(WarehouseLocation location);
        Task UpdateAsync(WarehouseLocation location);
        Task SaveChangesAsync();
    }
}
