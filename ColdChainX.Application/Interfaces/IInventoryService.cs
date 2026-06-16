using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Interfaces
{
    public interface IInventoryService
    {
        Task<ApiResponse<bool>> RelocateStockAsync(StockRelocationRequest request, Guid userId);
        Task<ApiResponse<bool>> AdjustStockAsync(InventoryAdjustmentRequest request, Guid userId, bool autoApprove = false);
        Task<ApiResponse<PagedResult<AvailableStockResponse>>> GetAvailableStockAsync(int pageNumber, int pageSize, string? itemCode = null);
        Task<ApiResponse<AllocationResultResponse>> AllocateStockAsync(AllocateInventoryRequest request, Guid userId);
        Task<ApiResponse<bool>> ReleaseAllocationAsync(ReleaseAllocationRequest request, Guid userId);

        Task<ApiResponse<Guid>> CreateAdjustmentRequestAsync(InventoryAdjustmentRequest request, Guid userId);
        Task<ApiResponse<bool>> ApproveAdjustmentAsync(Guid adjustmentId, Guid userId);
        Task<ApiResponse<bool>> RejectAdjustmentAsync(Guid adjustmentId, string reason, Guid userId);
        Task<ApiResponse<InventoryAdjustmentResponse>> GetAdjustmentByIdAsync(Guid adjustmentId);
        Task<ApiResponse<PagedResult<InventoryAdjustmentResponse>>> GetPagedAdjustmentsAsync(int pageNumber, int pageSize, InventoryAdjustmentStatus? status = null);

        Task<ApiResponse<List<PutawaySuggestionResponse>>> GetPutawaySuggestionsAsync(Guid stockId);
        Task<ApiResponse<List<StockPutawaySuggestionsResponse>>> GetPutawaySuggestionsByReceiptAsync(Guid receiptId);
    }
}
