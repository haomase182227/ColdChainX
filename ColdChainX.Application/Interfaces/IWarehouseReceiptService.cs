using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IWarehouseReceiptService
    {
        Task<ApiResponse<WarehouseReceiptResponse>> ProcessInboundQCAsync(Guid orderId, Guid warehouseId, InboundQCRequest request, Guid receiverId);
        Task<ApiResponse<WarehouseReceiptResponse>> UpdateMeasurementsAsync(Guid orderId, UpdateMeasurementsRequest request);
        Task<ApiResponse<WarehouseReceiptResponse>> CompleteInboundAsync(Guid orderId);
    }
}
