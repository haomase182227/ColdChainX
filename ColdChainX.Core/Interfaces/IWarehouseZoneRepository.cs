using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Core.Interfaces
{
    public interface IWarehouseZoneRepository
    {
        Task<WarehouseZone?> GetByIdAsync(Guid zoneId, bool includeDeleted = false);
        Task<WarehouseZone?> GetByCodeAsync(Guid warehouseId, string zoneCode, bool includeDeleted = false);
        Task<bool> ExistsByCodeAsync(Guid warehouseId, string zoneCode, Guid? excludeZoneId = null);
        Task<(IReadOnlyCollection<WarehouseZone> Data, int TotalCount)> GetListAsync(Guid warehouseId, int pageNumber, int pageSize, string? search = null);
        Task AddAsync(WarehouseZone zone);
        Task UpdateAsync(WarehouseZone zone);
        Task SaveChangesAsync();
    }
}
