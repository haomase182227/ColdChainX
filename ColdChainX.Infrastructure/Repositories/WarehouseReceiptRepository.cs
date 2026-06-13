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
    public class WarehouseReceiptRepository : IWarehouseReceiptRepository
    {
        private readonly ApplicationDbContext _db;

        public WarehouseReceiptRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<WarehouseReceipt?> GetByIdAsync(Guid receiptId)
        {
            return await _db.WarehouseReceipts
                .IgnoreQueryFilters()
                .Include(r => r.Warehouse)
                .Include(r => r.Order)
                .Include(r => r.WarehouseReceiptItems)
                .FirstOrDefaultAsync(r => r.ReceiptId == receiptId);
        }

        public async Task<WarehouseReceipt?> GetByOrderIdAsync(Guid orderId)
        {
            return await _db.WarehouseReceipts
                .IgnoreQueryFilters()
                .Include(r => r.Warehouse)
                .Include(r => r.Order)
                .Include(r => r.WarehouseReceiptItems)
                .FirstOrDefaultAsync(r => r.OrderId == orderId);
        }

        public async Task<List<WarehouseReceipt>> GetActiveReceiptsByWarehouseIdAsync(Guid warehouseId)
        {
            // Fetch receipts that are completed/active at the warehouse
            return await _db.WarehouseReceipts
                .Where(r => r.WarehouseId == warehouseId)
                .Include(r => r.WarehouseReceiptItems)
                .ToListAsync();
        }

        public async Task AddAsync(WarehouseReceipt receipt)
        {
            await _db.WarehouseReceipts.AddAsync(receipt);
        }

        public async Task AddItemAsync(WarehouseReceiptItem item)
        {
            await _db.WarehouseReceiptItems.AddAsync(item);
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
