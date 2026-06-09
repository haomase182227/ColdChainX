using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Core.Interfaces
{
    public interface IWarehouseReceiptRepository
    {
        Task<WarehouseReceipt?> GetByIdAsync(Guid receiptId);
        Task<WarehouseReceipt?> GetByOrderIdAsync(Guid orderId);
        Task<List<WarehouseReceipt>> GetActiveReceiptsByWarehouseIdAsync(Guid warehouseId);
        Task AddAsync(WarehouseReceipt receipt);
        Task AddItemAsync(WarehouseReceiptItem item);
        Task SaveChangesAsync();
    }
}
