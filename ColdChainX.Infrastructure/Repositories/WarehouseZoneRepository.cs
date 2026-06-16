using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;

namespace ColdChainX.Infrastructure.Repositories
{
    public class WarehouseZoneRepository : IWarehouseZoneRepository
    {
        private readonly ApplicationDbContext _db;

        public WarehouseZoneRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<WarehouseZone?> GetByIdAsync(Guid zoneId, bool includeDeleted = false)
        {
            var query = _db.WarehouseZones.AsQueryable();
            if (includeDeleted)
            {
                query = query.IgnoreQueryFilters();
            }
            return await query
                .Include(z => z.Warehouse)
                .FirstOrDefaultAsync(z => z.ZoneId == zoneId);
        }

        public async Task<WarehouseZone?> GetByCodeAsync(Guid warehouseId, string zoneCode, bool includeDeleted = false)
        {
            var query = _db.WarehouseZones.AsQueryable();
            if (includeDeleted)
            {
                query = query.IgnoreQueryFilters();
            }
            var code = zoneCode.Trim().ToLower();
            return await query
                .Include(z => z.Warehouse)
                .FirstOrDefaultAsync(z => z.WarehouseId == warehouseId && z.ZoneCode.ToLower() == code);
        }

        public async Task<bool> ExistsByCodeAsync(Guid warehouseId, string zoneCode, Guid? excludeZoneId = null)
        {
            var code = zoneCode.Trim().ToLower();
            var query = _db.WarehouseZones.AsNoTracking()
                .Where(z => z.WarehouseId == warehouseId && z.ZoneCode.ToLower() == code);

            if (excludeZoneId.HasValue)
            {
                query = query.Where(z => z.ZoneId != excludeZoneId.Value);
            }

            return await query.AnyAsync();
        }

        public async Task<(IReadOnlyCollection<WarehouseZone> Data, int TotalCount)> GetListAsync(Guid warehouseId, int pageNumber, int pageSize, string? search = null)
        {
            var query = _db.WarehouseZones
                .AsNoTracking()
                .Include(z => z.Warehouse)
                .Where(z => z.WarehouseId == warehouseId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var cleanSearch = search.Trim().ToLower();
                query = query.Where(z => z.ZoneName.ToLower().Contains(cleanSearch)
                                      || z.ZoneCode.ToLower().Contains(cleanSearch));
            }

            var totalCount = await query.CountAsync();

            var pageNum = pageNumber <= 0 ? 1 : pageNumber;
            var size = pageSize <= 0 ? 10 : pageSize;

            var data = await query
                .OrderBy(z => z.ZoneCode)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .ToListAsync();

            return (data, totalCount);
        }

        public async Task AddAsync(WarehouseZone zone)
        {
            await _db.WarehouseZones.AddAsync(zone);
        }

        public async Task UpdateAsync(WarehouseZone zone)
        {
            _db.WarehouseZones.Update(zone);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
