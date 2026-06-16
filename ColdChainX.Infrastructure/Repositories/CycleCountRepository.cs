using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;

namespace ColdChainX.Infrastructure.Repositories
{
    public class CycleCountRepository : ICycleCountRepository
    {
        private readonly ApplicationDbContext _db;

        public CycleCountRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task AddPlanAsync(CycleCountPlan plan)
        {
            await _db.CycleCountPlans.AddAsync(plan);
        }

        public async Task<CycleCountPlan?> GetPlanByIdAsync(Guid planId)
        {
            return await _db.CycleCountPlans
                .Include(p => p.Warehouse)
                .Include(p => p.AssignedToUser)
                .Include(p => p.Creator)
                .Include(p => p.Entries)
                    .ThenInclude(e => e.Location)
                .Include(p => p.Entries)
                    .ThenInclude(e => e.Stock)
                .Include(p => p.Entries)
                    .ThenInclude(e => e.Batch)
                .FirstOrDefaultAsync(p => p.PlanId == planId);
        }

        public async Task<CycleCountPlan?> GetPlanByCodeAsync(string planCode)
        {
            return await _db.CycleCountPlans
                .Include(p => p.Warehouse)
                .FirstOrDefaultAsync(p => p.PlanCode == planCode);
        }

        public async Task<List<CycleCountPlan>> GetPagedPlansAsync(int pageNumber, int pageSize, CycleCountPlanStatus? status, Guid? warehouseId)
        {
            var query = ApplyFilters(_db.CycleCountPlans.Include(p => p.Warehouse).Include(p => p.AssignedToUser), status, warehouseId);
            return await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> CountPlansAsync(CycleCountPlanStatus? status, Guid? warehouseId)
        {
            var query = ApplyFilters(_db.CycleCountPlans.AsNoTracking(), status, warehouseId);
            return await query.CountAsync();
        }

        public async Task<CycleCountEntry?> GetEntryByIdAsync(Guid entryId)
        {
            return await _db.CycleCountEntries
                .Include(e => e.Plan)
                    .ThenInclude(p => p.Warehouse)
                .Include(e => e.Location)
                .Include(e => e.Stock)
                .Include(e => e.Batch)
                .FirstOrDefaultAsync(e => e.EntryId == entryId);
        }

        public async Task UpdatePlanAsync(CycleCountPlan plan)
        {
            _db.CycleCountPlans.Update(plan);
            await Task.CompletedTask;
        }

        public async Task UpdateEntryAsync(CycleCountEntry entry)
        {
            _db.CycleCountEntries.Update(entry);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }

        private IQueryable<CycleCountPlan> ApplyFilters(IQueryable<CycleCountPlan> query, CycleCountPlanStatus? status, Guid? warehouseId)
        {
            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);

            if (warehouseId.HasValue)
                query = query.Where(p => p.WarehouseId == warehouseId.Value);

            return query;
        }
    }
}
