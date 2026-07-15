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
    public class WarehouseRepository : IWarehouseRepository
    {
        private readonly ApplicationDbContext _db;

        public WarehouseRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<Warehouse?> GetByIdAsync(Guid warehouseId, bool includeDeleted = false)
        {
            var query = _db.Warehouses.AsQueryable();
            if (includeDeleted)
            {
                query = query.IgnoreQueryFilters();
            }
            return await query.FirstOrDefaultAsync(w => w.WarehouseId == warehouseId);
        }

        public async Task<Warehouse?> GetByCodeAsync(string warehouseCode, bool includeDeleted = false)
        {
            var query = _db.Warehouses.AsQueryable();
            if (includeDeleted)
            {
                query = query.IgnoreQueryFilters();
            }
            return await query.FirstOrDefaultAsync(w => w.WarehouseCode.ToLower() == warehouseCode.Trim().ToLower());
        }

        public async Task<bool> ExistsByCodeAsync(string warehouseCode, Guid? excludeWarehouseId = null)
        {
            var code = warehouseCode.Trim().ToLower();
            var query = _db.Warehouses.AsNoTracking().Where(w => w.WarehouseCode.ToLower() == code);

            if (excludeWarehouseId.HasValue)
            {
                query = query.Where(w => w.WarehouseId != excludeWarehouseId.Value);
            }

            return await query.AnyAsync();
        }

        public async Task<(IReadOnlyCollection<Warehouse> Data, int TotalCount)> GetListAsync(int pageNumber, int pageSize, string? search = null)
        {
            var query = _db.Warehouses.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var cleanSearch = search.Trim().ToLower();
                query = query.Where(w => w.WarehouseName.ToLower().Contains(cleanSearch)
                                      || w.WarehouseCode.ToLower().Contains(cleanSearch)
                                      || (w.Address != null && w.Address.ToLower().Contains(cleanSearch)));
            }

            var totalCount = await query.CountAsync();

            var pageNum = pageNumber <= 0 ? 1 : pageNumber;
            var size = pageSize <= 0 ? 10 : pageSize;

            var data = await query
                .OrderBy(w => w.WarehouseCode)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .ToListAsync();

            return (data, totalCount);
        }

        public async Task AddAsync(Warehouse warehouse)
        {
            await _db.Warehouses.AddAsync(warehouse);
        }

        public async Task UpdateAsync(Warehouse warehouse)
        {
            _db.Warehouses.Update(warehouse);
            await Task.CompletedTask;
        }

        public async Task<(IReadOnlyCollection<Lpn> Data, int TotalCount)> GetLpnsInWarehouseAsync(Guid warehouseId, int page, int pageSize)
        {
            var query = _db.Lpns
                .Include(l => l.Receipt)
                .Where(l => l.WarehouseId == warehouseId && l.State == ColdChainX.Core.Enums.LpnState.IN_STOCK)
                .AsNoTracking();

            var totalCount = await query.CountAsync();
            var lpns = await query
                .OrderBy(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (lpns, totalCount);
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
