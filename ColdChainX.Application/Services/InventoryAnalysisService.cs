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
using ColdChainX.Core.Enums;
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
                var today = DateTime.UtcNow;
                var warningThresholdDate = today.AddDays(warningDays);

                var query = _db.Lpns
                    .Include(l => l.Order)
                    .Include(l => l.Receipt)
                        .ThenInclude(r => r.Warehouse)
                    .Where(l => l.Quantity > 0 && (l.State == LpnState.IN_STOCK || l.State == LpnState.ALLOCATED));

                if (warehouseId.HasValue)
                {
                    query = query.Where(l => l.Receipt.WarehouseId == warehouseId.Value);
                }

                // Filter soon-to-expire batches using SlaDeadline as expiration threshold
                query = query.Where(l => l.SlaDeadline != null && l.SlaDeadline <= warningThresholdDate);

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(l => l.SlaDeadline)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var responseList = items.Select(s => new ExpiryAlertResponse
                {
                    StockId = s.LpnId,
                    ItemCode = s.Order.TrackingCode ?? string.Empty,
                    ItemName = s.Order.ItemName ?? string.Empty,
                    BatchId = Guid.Empty,
                    BatchNumber = "N/A",
                    ExpiryDate = s.SlaDeadline.HasValue ? DateOnly.FromDateTime(s.SlaDeadline.Value) : DateOnly.FromDateTime(DateTime.UtcNow),
                    QuantityOnHand = s.Quantity,
                    RemainingDays = s.SlaDeadline.HasValue ? (s.SlaDeadline.Value - today).Days : warningDays,
                    WarehouseName = s.Receipt.Warehouse?.WarehouseName ?? "Unknown",
                    LocationCode = s.StorageLocation ?? "N/A"
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

                var query = _db.Lpns
                    .Include(l => l.Order)
                    .Include(l => l.Receipt)
                        .ThenInclude(r => r.Warehouse)
                    .Where(l => l.Quantity > 0 && (l.State == LpnState.IN_STOCK || l.State == LpnState.ALLOCATED));

                if (warehouseId.HasValue)
                {
                    query = query.Where(l => l.Receipt.WarehouseId == warehouseId.Value);
                }

                // Filter stocks created or received before the threshold date
                query = query.Where(l => l.InboundTime != null && l.InboundTime <= thresholdDateTime);

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(l => l.InboundTime)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var responseList = items.Select(s => new AgingStockResponse
                {
                    StockId = s.LpnId,
                    ItemCode = s.Order.TrackingCode ?? string.Empty,
                    ItemName = s.Order.ItemName ?? string.Empty,
                    InboundDate = s.InboundTime ?? s.CreatedAt,
                    StorageDays = (int)(now - (s.InboundTime ?? s.CreatedAt)).TotalDays,
                    QuantityOnHand = s.Quantity,
                    PalletCount = 1,
                    WarehouseName = s.Receipt.Warehouse?.WarehouseName ?? "Unknown",
                    LocationCode = s.StorageLocation ?? "N/A"
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
                var query = _db.Lpns
                    .Include(l => l.Order)
                    .Include(l => l.Receipt)
                        .ThenInclude(r => r.Warehouse)
                    .Where(l => l.Quantity > 0 && (l.State == LpnState.IN_STOCK || l.State == LpnState.ALLOCATED));

                if (warehouseId.HasValue)
                {
                    query = query.Where(l => l.Receipt.WarehouseId == warehouseId.Value);
                }

                var items = await query.ToListAsync();

                // Load the active locations and zones for temperature comparison
                var dbLocations = await _db.WarehouseLocations
                    .Include(l => l.Zone)
                    .ToListAsync();

                var locationMap = dbLocations
                    .Where(loc => !string.IsNullOrEmpty(loc.LocationCode))
                    .GroupBy(loc => loc.LocationCode.ToUpperInvariant())
                    .ToDictionary(g => g.Key, g => g.First());

                var responseList = new List<TempAuditResponse>();

                foreach (var s in items)
                {
                    var locCode = s.StorageLocation?.ToUpperInvariant() ?? string.Empty;
                    locationMap.TryGetValue(locCode, out var loc);

                    // Check if the required temperature matches the zone range
                    decimal? reqMin = s.RequiredTemperature;
                    decimal? reqMax = s.RequiredTemperature;

                    decimal? zoneMin = loc?.Zone?.TemperatureMin;
                    decimal? zoneMax = loc?.Zone?.TemperatureMax;

                    bool isFlagged = false;
                    if (reqMin.HasValue)
                    {
                        if (zoneMax.HasValue && reqMin.Value > zoneMax.Value) isFlagged = true;
                        if (zoneMin.HasValue && reqMin.Value < zoneMin.Value) isFlagged = true;
                    }

                    if (isFlagged)
                    {
                        responseList.Add(new TempAuditResponse
                        {
                            StockId = s.LpnId,
                            ItemCode = s.Order.TrackingCode ?? string.Empty,
                            ItemName = s.Order.ItemName ?? string.Empty,
                            RequiredTempMin = reqMin,
                            RequiredTempMax = reqMax,
                            ZoneTemperatureMin = zoneMin,
                            ZoneTemperatureMax = zoneMax,
                            ZoneCode = loc?.Zone?.ZoneCode ?? "Unknown",
                            LocationCode = s.StorageLocation ?? "N/A",
                            WarehouseName = s.Receipt.Warehouse?.WarehouseName ?? "Unknown"
                        });
                    }
                }

                int totalCount = responseList.Count;
                var pagedItems = responseList
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var pagedResult = PagedResult<TempAuditResponse>.Create(pagedItems, totalCount, pageNumber, pageSize);
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
