using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IInventoryAnalysisService
    {
        Task<ApiResponse<PagedResult<ExpiryAlertResponse>>> GetExpiryAlertsAsync(Guid? warehouseId, int warningDays, int pageNumber, int pageSize);

        Task<ApiResponse<PagedResult<AgingStockResponse>>> GetAgingInventoryAsync(Guid? warehouseId, int thresholdDays, int pageNumber, int pageSize);

        Task<ApiResponse<PagedResult<TempAuditResponse>>> GetTemperatureAuditsAsync(Guid? warehouseId, int pageNumber, int pageSize);

        Task<ApiResponse<WarehouseUtilizationResponse>> GetWarehouseUtilizationAsync(Guid warehouseId);
    }
}
