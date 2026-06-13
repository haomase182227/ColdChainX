using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IInventoryService
    {
        Task<ApiResponse<bool>> RelocateStockAsync(StockRelocationRequest request, Guid userId);
        Task<ApiResponse<bool>> AdjustStockAsync(InventoryAdjustmentRequest request, Guid userId);
        Task<ApiResponse<PagedResult<AvailableStockResponse>>> GetAvailableStockAsync(int pageNumber, int pageSize, string? itemCode = null);
        Task<ApiResponse<AllocationResultResponse>> AllocateStockAsync(AllocateInventoryRequest request, Guid userId);
        Task<ApiResponse<bool>> ReleaseAllocationAsync(ReleaseAllocationRequest request, Guid userId);
    }
}
