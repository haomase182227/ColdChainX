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
    public class InventoryHoldRepository : IInventoryHoldRepository
    {
        private readonly ApplicationDbContext _db;

        public InventoryHoldRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task AddAsync(InventoryHold hold)
        {
            await _db.InventoryHolds.AddAsync(hold);
        }

        public async Task UpdateAsync(InventoryHold hold)
        {
            _db.InventoryHolds.Update(hold);
            await Task.CompletedTask;
        }

        public async Task<InventoryHold?> GetByIdAsync(Guid holdId)
        {
            return await _db.InventoryHolds
                .Include(h => h.Stock)
                    .ThenInclude(s => s.Location)
                .FirstOrDefaultAsync(h => h.HoldId == holdId);
        }

        public async Task<List<InventoryHold>> GetPagedHoldsAsync(int pageNumber, int pageSize, string? status, string? reasonCode, string? itemCode)
        {
            var query = ApplyFilters(_db.InventoryHolds.Include(h => h.Stock).ThenInclude(s => s.Location), status, reasonCode, itemCode);
            return await query
                .OrderByDescending(h => h.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> CountHoldsAsync(string? status, string? reasonCode, string? itemCode)
        {
            var query = ApplyFilters(_db.InventoryHolds.AsNoTracking(), status, reasonCode, itemCode);
            return await query.CountAsync();
        }

        public async Task<List<InventoryHold>> GetActiveHoldsByStockIdAsync(Guid stockId)
        {
            return await _db.InventoryHolds
                .Where(h => h.StockId == stockId && h.Status == "HOLD")
                .ToListAsync();
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }

        private IQueryable<InventoryHold> ApplyFilters(IQueryable<InventoryHold> query, string? status, string? reasonCode, string? itemCode)
        {
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(h => h.Status == status);

            if (!string.IsNullOrWhiteSpace(reasonCode))
                query = query.Where(h => h.ReasonCode == reasonCode);

            if (!string.IsNullOrWhiteSpace(itemCode))
                query = query.Where(h => h.Stock.ItemCode == itemCode);

            return query;
        }
    }
}
