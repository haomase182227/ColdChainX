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
    public class WarehouseLocationRepository : IWarehouseLocationRepository
    {
        private readonly ApplicationDbContext _db;

        public WarehouseLocationRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<WarehouseLocation?> GetByIdAsync(Guid locationId, bool includeDeleted = false)
        {
            var query = _db.WarehouseLocations.AsQueryable();
            if (includeDeleted)
            {
                query = query.IgnoreQueryFilters();
            }
            return await query
                .Include(l => l.Zone)
                    .ThenInclude(z => z.Warehouse)
                .FirstOrDefaultAsync(l => l.LocationId == locationId);
        }

        public async Task<bool> ExistsByCodeAsync(Guid zoneId, string locationCode, Guid? excludeLocationId = null)
        {
            var code = locationCode.Trim().ToLower();
            var query = _db.WarehouseLocations.AsNoTracking()
                .Where(l => l.ZoneId == zoneId && l.LocationCode.ToLower() == code);

            if (excludeLocationId.HasValue)
            {
                query = query.Where(l => l.LocationId != excludeLocationId.Value);
            }

            return await query.AnyAsync();
        }

        public async Task<(IReadOnlyCollection<WarehouseLocation> Data, int TotalCount)> GetListAsync(
            Guid zoneId, int pageNumber, int pageSize, string? search = null)
        {
            var query = _db.WarehouseLocations
                .AsNoTracking()
                .Include(l => l.Zone)
                    .ThenInclude(z => z.Warehouse)
                .Where(l => l.ZoneId == zoneId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var cleanSearch = search.Trim().ToLower();
                query = query.Where(l =>
                    l.LocationCode.ToLower().Contains(cleanSearch) ||
                    (l.RackCode != null && l.RackCode.ToLower().Contains(cleanSearch)) ||
                    (l.BayCode != null && l.BayCode.ToLower().Contains(cleanSearch)));
            }

            var totalCount = await query.CountAsync();

            var pageNum = pageNumber <= 0 ? 1 : pageNumber;
            var size = pageSize <= 0 ? 10 : pageSize;

            var data = await query
                .OrderBy(l => l.LocationCode)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .ToListAsync();

            return (data, totalCount);
        }

        public async Task AddAsync(WarehouseLocation location)
        {
            await _db.WarehouseLocations.AddAsync(location);
        }

        public async Task UpdateAsync(WarehouseLocation location)
        {
            _db.WarehouseLocations.Update(location);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
