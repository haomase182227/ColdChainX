using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    /// <summary>
    /// Service interface for advanced inventory reports and capacity analysis.
    /// </summary>
    public interface IInventoryAnalysisService
    {
        /// <summary>
        /// Retrieves items that are close to their expiration dates.
        /// </summary>
        Task<ApiResponse<PagedResult<ExpiryAlertResponse>>> GetExpiryAlertsAsync(Guid? warehouseId, int warningDays, int pageNumber, int pageSize);

        /// <summary>
        /// Retrieves items that have been in storage longer than a threshold number of days.
        /// </summary>
        Task<ApiResponse<PagedResult<AgingStockResponse>>> GetAgingInventoryAsync(Guid? warehouseId, int thresholdDays, int pageNumber, int pageSize);

        /// <summary>
        /// Identifies inventory stocks stored in zones that do not match the required product temperature ranges.
        /// </summary>
        Task<ApiResponse<PagedResult<TempAuditResponse>>> GetTemperatureAuditsAsync(Guid? warehouseId, int pageNumber, int pageSize);

        /// <summary>
        /// Generates a warehouse utilization and occupancy report per zone.
        /// </summary>
        Task<ApiResponse<WarehouseUtilizationResponse>> GetWarehouseUtilizationAsync(Guid warehouseId);
    }
}
