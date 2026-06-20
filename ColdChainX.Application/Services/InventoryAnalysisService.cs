using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class InventoryAnalysisService : IInventoryAnalysisService
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<InventoryAnalysisService> _logger;

        public InventoryAnalysisService(IApplicationDbContext db, ILogger<InventoryAnalysisService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse<PagedResult<ExpiryAlertResponse>>> GetExpiryAlertsAsync(Guid? warehouseId, int warningDays, int pageNumber, int pageSize)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var warningThresholdDate = today.AddDays(warningDays);

                var query = _db.InventoryStocks
                    .Include(s => s.Batch)
                    .Include(s => s.Location)
                        .ThenInclude(l => l.Zone)
                            .ThenInclude(z => z.Warehouse)
                    .Where(s => s.QuantityOnHand > 0 && s.Status == "AVAILABLE");

                if (warehouseId.HasValue)
                {
                    query = query.Where(s => s.Location.Zone.WarehouseId == warehouseId.Value);
                }

                // Filter soon-to-expire batches
                query = query.Where(s => s.Batch.ExpiryDate <= warningThresholdDate);

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(s => s.Batch.ExpiryDate)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var responseList = items.Select(s => new ExpiryAlertResponse
                {
                    StockId = s.StockId,
                    ItemCode = s.ItemCode,
                    ItemName = s.ItemName,
                    BatchId = s.BatchId,
                    BatchNumber = s.Batch.BatchNumber,
                    ExpiryDate = s.Batch.ExpiryDate,
                    QuantityOnHand = s.QuantityOnHand,
                    RemainingDays = s.Batch.ExpiryDate.DayNumber - today.DayNumber,
                    WarehouseName = s.Location.Zone.Warehouse.WarehouseName,
                    LocationCode = s.Location.LocationCode
                }).ToList();

                var pagedResult = PagedResult<ExpiryAlertResponse>.Create(responseList, totalCount, pageNumber, pageSize);
                return ApiResponse<PagedResult<ExpiryAlertResponse>>.SuccessResponse(pagedResult, "Expiry alerts retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve expiry alerts. WarehouseId: {WarehouseId}, WarningDays: {WarningDays}", warehouseId, warningDays);
                return ApiResponse<PagedResult<ExpiryAlertResponse>>.Failure($"Failed to retrieve expiry alerts: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PagedResult<AgingStockResponse>>> GetAgingInventoryAsync(Guid? warehouseId, int thresholdDays, int pageNumber, int pageSize)
        {
            try
            {
                var now = DateTime.UtcNow;
                var thresholdDateTime = now.AddDays(-thresholdDays);

                var query = _db.InventoryStocks
                    .Include(s => s.Location)
                        .ThenInclude(l => l.Zone)
                            .ThenInclude(z => z.Warehouse)
                    .Where(s => s.QuantityOnHand > 0 && s.Status == "AVAILABLE");

                if (warehouseId.HasValue)
                {
                    query = query.Where(s => s.Location.Zone.WarehouseId == warehouseId.Value);
                }

                // Filter stocks created or received before the threshold date
                query = query.Where(s => s.InboundDate <= thresholdDateTime);

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(s => s.InboundDate)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var responseList = items.Select(s => new AgingStockResponse
                {
                    StockId = s.StockId,
                    ItemCode = s.ItemCode,
                    ItemName = s.ItemName,
                    InboundDate = s.InboundDate,
                    StorageDays = (int)(now - s.InboundDate).TotalDays,
                    QuantityOnHand = s.QuantityOnHand,
                    PalletCount = s.PalletCount,
                    WarehouseName = s.Location.Zone.Warehouse.WarehouseName,
                    LocationCode = s.Location.LocationCode
                }).ToList();

                var pagedResult = PagedResult<AgingStockResponse>.Create(responseList, totalCount, pageNumber, pageSize);
                return ApiResponse<PagedResult<AgingStockResponse>>.SuccessResponse(pagedResult, "Aging inventory report retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve aging inventory. WarehouseId: {WarehouseId}, ThresholdDays: {ThresholdDays}", warehouseId, thresholdDays);
                return ApiResponse<PagedResult<AgingStockResponse>>.Failure($"Failed to retrieve aging inventory: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PagedResult<TempAuditResponse>>> GetTemperatureAuditsAsync(Guid? warehouseId, int pageNumber, int pageSize)
        {
            try
            {
                var query = _db.InventoryStocks
                    .Include(s => s.Location)
                        .ThenInclude(l => l.Zone)
                            .ThenInclude(z => z.Warehouse)
                    .Where(s => s.QuantityOnHand > 0 && s.Status == "AVAILABLE");

                if (warehouseId.HasValue)
                {
                    query = query.Where(s => s.Location.Zone.WarehouseId == warehouseId.Value);
                }

                // Flag if stock required range is incompatible with zone capability
                query = query.Where(s =>
                    (s.RequiredTempMin.HasValue && s.Location.Zone.TemperatureMax.HasValue && s.RequiredTempMin.Value > s.Location.Zone.TemperatureMax.Value) ||
                    (s.RequiredTempMax.HasValue && s.Location.Zone.TemperatureMin.HasValue && s.RequiredTempMax.Value < s.Location.Zone.TemperatureMin.Value)
                );

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(s => s.ItemCode)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var responseList = items.Select(s => new TempAuditResponse
                {
                    StockId = s.StockId,
                    ItemCode = s.ItemCode,
                    ItemName = s.ItemName,
                    RequiredTempMin = s.RequiredTempMin,
                    RequiredTempMax = s.RequiredTempMax,
                    ZoneTemperatureMin = s.Location.Zone.TemperatureMin,
                    ZoneTemperatureMax = s.Location.Zone.TemperatureMax,
                    ZoneCode = s.Location.Zone.ZoneCode,
                    LocationCode = s.Location.LocationCode,
                    WarehouseName = s.Location.Zone.Warehouse.WarehouseName
                }).ToList();

                var pagedResult = PagedResult<TempAuditResponse>.Create(responseList, totalCount, pageNumber, pageSize);
                return ApiResponse<PagedResult<TempAuditResponse>>.SuccessResponse(pagedResult, "Temperature audits completed and flagged items retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run temperature audits. WarehouseId: {WarehouseId}", warehouseId);
                return ApiResponse<PagedResult<TempAuditResponse>>.Failure($"Failed to run temperature audits: {ex.Message}");
            }
        }

        public async Task<ApiResponse<WarehouseUtilizationResponse>> GetWarehouseUtilizationAsync(Guid warehouseId)
        {
            try
            {
                var warehouse = await _db.Warehouses
                    .Include(w => w.WarehouseZones)
                    .FirstOrDefaultAsync(w => w.WarehouseId == warehouseId);

                if (warehouse == null)
                {
                    return ApiResponse<WarehouseUtilizationResponse>.Failure("Warehouse not found.");
                }

                var zones = await _db.WarehouseZones
                    .Where(z => z.WarehouseId == warehouseId && z.Status == "ACTIVE")
                    .ToListAsync();

                int currentPallets = zones.Sum(z => z.CurrentPallets);
                int maxCapacity = warehouse.MaxPallets;
                double warehouseOccupancy = maxCapacity > 0 ? (double)currentPallets / maxCapacity : 0.0;

                var zoneOccupancyDetails = zones.Select(z => new ZoneOccupancyDetail
                {
                    ZoneId = z.ZoneId,
                    ZoneCode = z.ZoneCode,
                    ZoneName = z.ZoneName,
                    MaxCapacityPallets = z.MaxCapacityPallets,
                    CurrentPallets = z.CurrentPallets,
                    ZoneOccupancyRate = z.MaxCapacityPallets > 0 ? (double)z.CurrentPallets / z.MaxCapacityPallets : 0.0
                }).ToList();

                var response = new WarehouseUtilizationResponse
                {
                    WarehouseId = warehouse.WarehouseId,
                    WarehouseCode = warehouse.WarehouseCode,
                    WarehouseName = warehouse.WarehouseName,
                    MaxPallets = maxCapacity,
                    CurrentPallets = currentPallets,
                    WarehouseOccupancyRate = warehouseOccupancy,
                    ZoneOccupancyRates = zoneOccupancyDetails
                };

                return ApiResponse<WarehouseUtilizationResponse>.SuccessResponse(response, "Warehouse capacity utilization report generated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate warehouse utilization. WarehouseId: {WarehouseId}", warehouseId);
                return ApiResponse<WarehouseUtilizationResponse>.Failure($"Failed to generate warehouse utilization report: {ex.Message}");
            }
        }
    }
}
