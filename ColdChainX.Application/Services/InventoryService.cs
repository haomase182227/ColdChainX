using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.DTOs.WarehouseReceipt;

namespace ColdChainX.Application.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(IApplicationDbContext db, ILogger<InventoryService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse<bool>> RelocateStockAsync(StockRelocationRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<bool>.Failure("Request is null");

            if (request.SourceLocationId == request.DestinationLocationId)
                return ApiResponse<bool>.Failure("Source and destination locations cannot be identical.");

            var isOuterTransaction = _db.Database.CurrentTransaction == null;
            using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                // 1. Fetch Source Stock and Destination Location without locks first to get Zone IDs.
                var tempStock = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .FirstOrDefaultAsync(s => s.LocationId == request.SourceLocationId 
                                              && s.ItemCode == request.ItemCode 
                                              && s.BatchId == request.BatchId 
                                              && s.Status == "AVAILABLE");

                if (tempStock == null)
                    return ApiResponse<bool>.Failure($"Source stock not found for item '{request.ItemCode}' and batch '{request.BatchId}' in the specified location.");

                var tempDestLocation = await _db.WarehouseLocations
                    .FirstOrDefaultAsync(l => l.LocationId == request.DestinationLocationId);

                if (tempDestLocation == null)
                    return ApiResponse<bool>.Failure("Destination location not found.");

                var sourceZoneId = tempStock.Location.ZoneId;
                var destZoneId = tempDestLocation.ZoneId;

                // Sort Zone and Location IDs to lock deterministically (prevents deadlocks)
                var zoneIdsToLock = new List<Guid> { sourceZoneId, destZoneId }.Distinct().OrderBy(id => id).ToList();
                var locationIdsToLock = new List<Guid> { request.SourceLocationId, request.DestinationLocationId }.Distinct().OrderBy(id => id).ToList();

                // Apply pessimistic locks
                if (_db.Database.IsRelational())
                {
                    foreach (var zoneId in zoneIdsToLock)
                    {
                        await _db.WarehouseZones
                            .FromSqlRaw("SELECT * FROM warehouse_zones WHERE zone_id = {0} FOR UPDATE", zoneId)
                            .FirstOrDefaultAsync();
                    }

                    foreach (var locId in locationIdsToLock)
                    {
                        await _db.WarehouseLocations
                            .FromSqlRaw("SELECT * FROM warehouse_locations WHERE location_id = {0} FOR UPDATE", locId)
                            .FirstOrDefaultAsync();
                    }
                }

                // 2. Fetch Source Stock with Batch to verify details under lock
                var sourceStock = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .ThenInclude(l => l.Zone)
                    .FirstOrDefaultAsync(s => s.LocationId == request.SourceLocationId 
                                              && s.ItemCode == request.ItemCode 
                                              && s.BatchId == request.BatchId 
                                              && s.Status == "AVAILABLE");

                if (sourceStock == null)
                    return ApiResponse<bool>.Failure($"Source stock not found for item '{request.ItemCode}' and batch '{request.BatchId}' in the specified location.");

                // Check available quantity
                decimal availableQty = sourceStock.QuantityOnHand - sourceStock.QuantityAllocated;
                if (availableQty < request.Quantity)
                    return ApiResponse<bool>.Failure($"Insufficient stock. Requested: {request.Quantity}, Available (unallocated): {availableQty}.");

                // 3. Fetch Destination Location & Zone under lock
                var destLocation = await _db.WarehouseLocations
                    .Include(l => l.Zone)
                    .FirstOrDefaultAsync(l => l.LocationId == request.DestinationLocationId);

                if (destLocation == null)
                    return ApiResponse<bool>.Failure("Destination location not found.");

                if (destLocation.Status != "ACTIVE")
                    return ApiResponse<bool>.Failure("Destination location is not active.");

                var destZone = destLocation.Zone;
                if (destZone == null)
                    return ApiResponse<bool>.Failure("Destination zone not found.");

                if (destZone.Status != "ACTIVE")
                    return ApiResponse<bool>.Failure("Destination zone is not active.");

                // Cap relocation pallets to source stock pallet count to prevent pallet drift
                int palletsToMove = Math.Min(request.Pallets, sourceStock.PalletCount);
                if (palletsToMove < 0)
                    palletsToMove = 0;

                // 4. Validation: Destination Location Capacity Check
                if (destLocation.CurrentPallets + palletsToMove > destLocation.MaxCapacityPallets)
                {
                    return ApiResponse<bool>.Failure($"Capacity exceeded: Destination location '{destLocation.LocationCode}' does not have enough capacity. Current: {destLocation.CurrentPallets}, Adding: {palletsToMove}, Max: {destLocation.MaxCapacityPallets}.");
                }

                // 5. Validation: Destination Zone Capacity Check
                if (destZone.CurrentPallets + palletsToMove > destZone.MaxCapacityPallets)
                {
                    return ApiResponse<bool>.Failure($"Capacity exceeded: Destination zone '{destZone.ZoneCode}' does not have enough capacity. Current: {destZone.CurrentPallets}, Adding: {palletsToMove}, Max: {destZone.MaxCapacityPallets}.");
                }

                // 6. Validation: Destination Zone Temperature Compatibility Check
                if (sourceStock.RequiredTempMin.HasValue && destZone.TemperatureMax.HasValue && sourceStock.RequiredTempMin.Value > destZone.TemperatureMax.Value)
                {
                    return ApiResponse<bool>.Failure($"Temperature incompatible: Stock requires min temp {sourceStock.RequiredTempMin.Value}°C, but destination zone max temp is {destZone.TemperatureMax.Value}°C.");
                }

                if (sourceStock.RequiredTempMax.HasValue && destZone.TemperatureMin.HasValue && sourceStock.RequiredTempMax.Value < destZone.TemperatureMin.Value)
                {
                    return ApiResponse<bool>.Failure($"Temperature incompatible: Stock requires max temp {sourceStock.RequiredTempMax.Value}°C, but destination zone min temp is {destZone.TemperatureMin.Value}°C.");
                }

                // 7. Deduct Quantity and Pallets from Source Stock
                var sourceZone = sourceStock.Location.Zone;
                
                sourceStock.QuantityOnHand -= request.Quantity;
                sourceStock.PalletCount -= palletsToMove;
                if (sourceStock.PalletCount < 0)
                    sourceStock.PalletCount = 0;

                sourceStock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                sourceStock.UpdatedBy = userId;

                // Apply Stock Depletion Rule (Status-based)
                if (sourceStock.QuantityOnHand == 0)
                {
                    sourceStock.Status = "INACTIVE";
                    sourceStock.PalletCount = 0;
                }

                // Update Pallets in Locations and Zones
                destLocation.CurrentPallets += palletsToMove;
                destZone.CurrentPallets += palletsToMove;

                sourceStock.Location.CurrentPallets -= palletsToMove;
                if (sourceStock.Location.CurrentPallets < 0)
                    sourceStock.Location.CurrentPallets = 0;

                if (sourceZone != null)
                {
                    sourceZone.CurrentPallets -= palletsToMove;
                    if (sourceZone.CurrentPallets < 0)
                        sourceZone.CurrentPallets = 0;
                }

                // 8. Find or Create Destination Stock (Status = "AVAILABLE")
                var destStock = await _db.InventoryStocks
                    .FirstOrDefaultAsync(s => s.LocationId == request.DestinationLocationId 
                                              && s.ItemCode == request.ItemCode 
                                              && s.BatchId == request.BatchId);

                if (destStock == null)
                {
                    destStock = new InventoryStock
                    {
                        StockId = Guid.NewGuid(),
                        LocationId = request.DestinationLocationId,
                        CustomerId = sourceStock.CustomerId,
                        ItemCode = request.ItemCode,
                        ItemName = sourceStock.ItemName,
                        Unit = sourceStock.Unit,
                        BatchId = request.BatchId,
                        QuantityOnHand = request.Quantity,
                        QuantityAllocated = 0,
                        InboundDate = sourceStock.InboundDate, // preserve original inbound date for FIFO/FEFO
                        Status = "AVAILABLE",
                        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                        CreatedBy = userId,
                        PalletCount = palletsToMove,
                        RequiredTempMin = sourceStock.RequiredTempMin,
                        RequiredTempMax = sourceStock.RequiredTempMax
                    };
                    _db.InventoryStocks.Add(destStock);
                }
                else
                {
                    destStock.QuantityOnHand += request.Quantity;
                    destStock.PalletCount += palletsToMove;
                    destStock.Status = "AVAILABLE"; // Reactivate if it was INACTIVE
                    destStock.RequiredTempMin = sourceStock.RequiredTempMin;
                    destStock.RequiredTempMax = sourceStock.RequiredTempMax;
                    destStock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    destStock.UpdatedBy = userId;
                }

                // 9. Log InventoryMovement
                string movementType = (sourceStock.Location.LocationCode == "RCV-STAGE-01" || sourceZone?.ZoneType == "RECEIVING") 
                    ? "PUTAWAY" 
                    : "RELOCATION";

                var movement = new InventoryMovement
                {
                    MovementId = Guid.NewGuid(),
                    StockId = sourceStock.StockId,
                    ItemCode = request.ItemCode,
                    BatchId = request.BatchId,
                    MovementType = movementType,
                    Quantity = request.Quantity,
                    FromLocationId = request.SourceLocationId,
                    ToLocationId = request.DestinationLocationId,
                    ReferenceDocumentId = null,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    CreatedBy = userId
                };
                _db.InventoryMovements.Add(movement);

                await _db.SaveChangesAsync();
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.CommitAsync();
                }

                _logger.LogInformation("Stock relocated successfully. MovementType: {MovementType}, Quantity: {Quantity}, Item: {ItemCode}, BatchId: {BatchId}, From: {FromLocationId}, To: {ToLocationId}",
                    movementType, request.Quantity, request.ItemCode, request.BatchId, request.SourceLocationId, request.DestinationLocationId);

                return ApiResponse<bool>.SuccessResponse(true, $"Stock relocated successfully via {movementType}.");
            }
            catch (Exception ex)
            {
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                _logger.LogError(ex, "Failed to relocate stock for item '{ItemCode}' and batch '{BatchId}'", request.ItemCode, request.BatchId);
                return ApiResponse<bool>.Failure($"Failed to relocate stock: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> AdjustStockAsync(InventoryAdjustmentRequest request, Guid userId, bool autoApprove = false)
        {
            if (request == null)
                return ApiResponse<bool>.Failure("Request is null");

            if (!autoApprove)
            {
                var createResult = await CreateAdjustmentRequestAsync(request, userId);
                if (!createResult.Success)
                    return ApiResponse<bool>.Failure(createResult.Message);
                return ApiResponse<bool>.SuccessResponse(true, "Stock adjustment request submitted successfully for approval.");
            }

            var isOuterTransaction = _db.Database.CurrentTransaction == null;
            using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                // 1. Fetch Existing Stock directly by StockId
                var stock = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .ThenInclude(l => l.Zone)
                    .FirstOrDefaultAsync(s => s.StockId == request.StockId);

                if (stock == null)
                    return ApiResponse<bool>.Failure("Stock record not found.");

                // 2. Lock Location and Zone to prevent capacity/pallets race conditions
                if (stock.Location != null && _db.Database.IsRelational())
                {
                    if (stock.Location.ZoneId != Guid.Empty)
                    {
                        await _db.WarehouseZones
                            .FromSqlRaw("SELECT * FROM warehouse_zones WHERE zone_id = {0} FOR UPDATE", stock.Location.ZoneId)
                            .FirstOrDefaultAsync();
                    }
                    await _db.WarehouseLocations
                        .FromSqlRaw("SELECT * FROM warehouse_locations WHERE location_id = {0} FOR UPDATE", stock.LocationId)
                        .FirstOrDefaultAsync();
                }

                // 3. Calculate Qty Delta and Pallet Delta
                decimal deltaQty = request.IsAbsoluteCount
                    ? request.Quantity - stock.QuantityOnHand
                    : request.Quantity;

                int deltaPallets = request.IsAbsoluteCount
                    ? request.Pallets - stock.PalletCount
                    : request.Pallets;

                // 4. Validation: Quantity and Pallet Count must not become negative
                if (stock.QuantityOnHand + deltaQty < 0)
                {
                    return ApiResponse<bool>.Failure($"Adjustment failed: Resulting quantity cannot be negative. Current: {stock.QuantityOnHand}, Delta: {deltaQty}.");
                }

                if (stock.PalletCount + deltaPallets < 0)
                {
                    return ApiResponse<bool>.Failure($"Adjustment failed: Resulting pallet count cannot be negative. Current: {stock.PalletCount}, Delta: {deltaPallets}.");
                }

                // 5. Validation: Location and Zone Capacity checks when adding pallets
                var location = stock.Location;
                var zone = location?.Zone;

                if (deltaPallets > 0)
                {
                    if (location != null && location.CurrentPallets + deltaPallets > location.MaxCapacityPallets)
                    {
                        return ApiResponse<bool>.Failure($"Capacity exceeded: Location '{location.LocationCode}' does not have enough capacity. Current: {location.CurrentPallets}, Adding: {deltaPallets}, Max: {location.MaxCapacityPallets}.");
                    }

                    if (zone != null && zone.CurrentPallets + deltaPallets > zone.MaxCapacityPallets)
                    {
                        return ApiResponse<bool>.Failure($"Capacity exceeded: Zone '{zone.ZoneCode}' does not have enough capacity. Current: {zone.CurrentPallets}, Adding: {deltaPallets}, Max: {zone.MaxCapacityPallets}.");
                    }
                }

                // 6. Apply changes and save state snapshots
                decimal qtyBefore = stock.QuantityOnHand;
                stock.QuantityOnHand += deltaQty;
                decimal qtyAfter = stock.QuantityOnHand;

                int palletsBefore = stock.PalletCount;
                stock.PalletCount += deltaPallets;
                int palletsAfter = stock.PalletCount;

                // Update location and zone pallet occupancies
                if (location != null)
                {
                    location.CurrentPallets += deltaPallets;
                    if (location.CurrentPallets < 0)
                        location.CurrentPallets = 0;
                }

                if (zone != null)
                {
                    zone.CurrentPallets += deltaPallets;
                    if (zone.CurrentPallets < 0)
                        zone.CurrentPallets = 0;
                }

                // Apply Stock Depletion & Reactivation Rules (Do NOT set Status = HOLD)
                if (stock.QuantityOnHand == 0)
                {
                    stock.Status = "INACTIVE";
                    stock.PalletCount = 0;
                }
                else if (stock.Status == "INACTIVE" && stock.QuantityOnHand > 0)
                {
                    stock.Status = "AVAILABLE";
                }

                stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                stock.UpdatedBy = userId;

                // 7. Log InventoryMovement
                var movement = new InventoryMovement
                {
                    MovementId = Guid.NewGuid(),
                    StockId = stock.StockId,
                    ItemCode = stock.ItemCode,
                    BatchId = stock.BatchId,
                    MovementType = "ADJUSTMENT",
                    Quantity = Math.Abs(deltaQty),
                    FromLocationId = deltaQty < 0 ? stock.LocationId : null,
                    ToLocationId = deltaQty > 0 ? stock.LocationId : null,
                    ReferenceDocumentId = null,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    CreatedBy = userId
                };
                _db.InventoryMovements.Add(movement);

                // Save to generate movement ID
                await _db.SaveChangesAsync();

                // 8. Log InventoryAdjustment
                var adjustment = new InventoryAdjustment
                {
                    AdjustmentId = Guid.NewGuid(),
                    StockId = stock.StockId,
                    AdjustmentType = request.AdjustmentType,
                    QuantityBefore = qtyBefore,
                    QuantityChanged = deltaQty,
                    QuantityAfter = qtyAfter,
                    PalletsBefore = palletsBefore,
                    PalletsChanged = deltaPallets,
                    PalletsAfter = palletsAfter,
                    ReasonNotes = request.Reason.Trim(),
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    CreatedBy = userId,
                    MovementId = movement.MovementId,
                    Status = InventoryAdjustmentStatus.APPROVED,
                    ApprovedBy = userId,
                    ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                _db.InventoryAdjustments.Add(adjustment);

                await _db.SaveChangesAsync();
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.CommitAsync();
                }

                _logger.LogInformation("Stock adjusted successfully (auto-approved). StockId: {StockId}, AdjustmentType: {AdjustmentType}, QtyChanged: {QtyChanged}, PalletsChanged: {PalletsChanged}",
                    stock.StockId, request.AdjustmentType, deltaQty, deltaPallets);

                return ApiResponse<bool>.SuccessResponse(true, $"Stock adjusted successfully via {request.AdjustmentType}.");
            }
            catch (Exception ex)
            {
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                _logger.LogError(ex, "Failed to adjust stock. StockId: {StockId}, AdjustmentType: {AdjustmentType}", request.StockId, request.AdjustmentType);
                return ApiResponse<bool>.Failure($"Failed to adjust stock: {ex.Message}");
            }
        }

        public async Task<ApiResponse<Guid>> CreateAdjustmentRequestAsync(InventoryAdjustmentRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<Guid>.Failure("Request is null");

            try
            {
                var stock = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .ThenInclude(l => l.Zone)
                    .FirstOrDefaultAsync(s => s.StockId == request.StockId);

                if (stock == null)
                    return ApiResponse<Guid>.Failure("Stock record not found.");

                decimal deltaQty = request.IsAbsoluteCount
                    ? request.Quantity - stock.QuantityOnHand
                    : request.Quantity;

                int deltaPallets = request.IsAbsoluteCount
                    ? request.Pallets - stock.PalletCount
                    : request.Pallets;

                if (stock.QuantityOnHand + deltaQty < 0)
                {
                    return ApiResponse<Guid>.Failure($"Adjustment failed: Resulting quantity cannot be negative. Current: {stock.QuantityOnHand}, Delta: {deltaQty}.");
                }

                if (stock.PalletCount + deltaPallets < 0)
                {
                    return ApiResponse<Guid>.Failure($"Adjustment failed: Resulting pallet count cannot be negative. Current: {stock.PalletCount}, Delta: {deltaPallets}.");
                }

                var location = stock.Location;
                var zone = location?.Zone;
                if (deltaPallets > 0)
                {
                    if (location != null && location.CurrentPallets + deltaPallets > location.MaxCapacityPallets)
                    {
                        return ApiResponse<Guid>.Failure($"Capacity exceeded: Location '{location.LocationCode}' does not have enough capacity. Current: {location.CurrentPallets}, Adding: {deltaPallets}, Max: {location.MaxCapacityPallets}.");
                    }
                    if (zone != null && zone.CurrentPallets + deltaPallets > zone.MaxCapacityPallets)
                    {
                        return ApiResponse<Guid>.Failure($"Capacity exceeded: Zone '{zone.ZoneCode}' does not have enough capacity. Current: {zone.CurrentPallets}, Adding: {deltaPallets}, Max: {zone.MaxCapacityPallets}.");
                    }
                }

                var adjustment = new InventoryAdjustment
                {
                    AdjustmentId = Guid.NewGuid(),
                    StockId = stock.StockId,
                    AdjustmentType = request.AdjustmentType,
                    QuantityBefore = stock.QuantityOnHand,
                    QuantityChanged = deltaQty,
                    QuantityAfter = stock.QuantityOnHand + deltaQty,
                    PalletsBefore = stock.PalletCount,
                    PalletsChanged = deltaPallets,
                    PalletsAfter = stock.PalletCount + deltaPallets,
                    ReasonNotes = request.Reason.Trim(),
                    Status = InventoryAdjustmentStatus.PENDING_APPROVAL,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    CreatedBy = userId,
                    MovementId = null
                };

                _db.InventoryAdjustments.Add(adjustment);
                await _db.SaveChangesAsync();

                return ApiResponse<Guid>.SuccessResponse(adjustment.AdjustmentId, "Adjustment request submitted successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create adjustment request for StockId: {StockId}", request.StockId);
                return ApiResponse<Guid>.Failure($"Failed to create adjustment request: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> ApproveAdjustmentAsync(Guid adjustmentId, Guid userId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var adjustment = await _db.InventoryAdjustments
                    .Include(a => a.Stock)
                    .ThenInclude(s => s.Location)
                    .ThenInclude(l => l.Zone)
                    .FirstOrDefaultAsync(a => a.AdjustmentId == adjustmentId);

                if (adjustment == null)
                    return ApiResponse<bool>.Failure("Adjustment request not found.");

                if (adjustment.Status != InventoryAdjustmentStatus.PENDING_APPROVAL)
                    return ApiResponse<bool>.Failure($"Adjustment request is not pending approval. Current status: {adjustment.Status}");

                var stock = adjustment.Stock;
                if (stock == null)
                    return ApiResponse<bool>.Failure("Stock record not found.");

                // Lock Location and Zone to prevent capacity/pallets race conditions
                if (stock.Location != null && _db.Database.IsRelational())
                {
                    if (stock.Location.ZoneId != Guid.Empty)
                    {
                        await _db.WarehouseZones
                            .FromSqlRaw("SELECT * FROM warehouse_zones WHERE zone_id = {0} FOR UPDATE", stock.Location.ZoneId)
                            .FirstOrDefaultAsync();
                    }
                    await _db.WarehouseLocations
                        .FromSqlRaw("SELECT * FROM warehouse_locations WHERE location_id = {0} FOR UPDATE", stock.LocationId)
                        .FirstOrDefaultAsync();
                }

                if (stock.QuantityOnHand + adjustment.QuantityChanged < 0)
                {
                    return ApiResponse<bool>.Failure($"Adjustment failed: Resulting quantity cannot be negative. Current: {stock.QuantityOnHand}, Delta: {adjustment.QuantityChanged}.");
                }

                if (stock.PalletCount + adjustment.PalletsChanged < 0)
                {
                    return ApiResponse<bool>.Failure($"Adjustment failed: Resulting pallet count cannot be negative. Current: {stock.PalletCount}, Delta: {adjustment.PalletsChanged}.");
                }

                var location = stock.Location;
                var zone = location?.Zone;
                if (adjustment.PalletsChanged > 0)
                {
                    if (location != null && location.CurrentPallets + adjustment.PalletsChanged > location.MaxCapacityPallets)
                    {
                        return ApiResponse<bool>.Failure($"Capacity exceeded: Location '{location.LocationCode}' does not have enough capacity. Current: {location.CurrentPallets}, Adding: {adjustment.PalletsChanged}, Max: {location.MaxCapacityPallets}.");
                    }
                    if (zone != null && zone.CurrentPallets + adjustment.PalletsChanged > zone.MaxCapacityPallets)
                    {
                        return ApiResponse<bool>.Failure($"Capacity exceeded: Zone '{zone.ZoneCode}' does not have enough capacity. Current: {zone.CurrentPallets}, Adding: {adjustment.PalletsChanged}, Max: {zone.MaxCapacityPallets}.");
                    }
                }

                decimal qtyBefore = stock.QuantityOnHand;
                stock.QuantityOnHand += adjustment.QuantityChanged;
                decimal qtyAfter = stock.QuantityOnHand;

                int palletsBefore = stock.PalletCount;
                stock.PalletCount += adjustment.PalletsChanged;
                int palletsAfter = stock.PalletCount;

                if (location != null)
                {
                    location.CurrentPallets += adjustment.PalletsChanged;
                    if (location.CurrentPallets < 0) location.CurrentPallets = 0;
                }

                if (zone != null)
                {
                    zone.CurrentPallets += adjustment.PalletsChanged;
                    if (zone.CurrentPallets < 0) zone.CurrentPallets = 0;
                }

                if (stock.QuantityOnHand == 0)
                {
                    stock.Status = "INACTIVE";
                    stock.PalletCount = 0;
                }
                else if (stock.Status == "INACTIVE" && stock.QuantityOnHand > 0)
                {
                    stock.Status = "AVAILABLE";
                }

                stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                stock.UpdatedBy = userId;

                var movement = new InventoryMovement
                {
                    MovementId = Guid.NewGuid(),
                    StockId = stock.StockId,
                    ItemCode = stock.ItemCode,
                    BatchId = stock.BatchId,
                    MovementType = "ADJUSTMENT",
                    Quantity = Math.Abs(adjustment.QuantityChanged),
                    FromLocationId = adjustment.QuantityChanged < 0 ? stock.LocationId : null,
                    ToLocationId = adjustment.QuantityChanged > 0 ? stock.LocationId : null,
                    ReferenceDocumentId = null,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    CreatedBy = userId
                };
                _db.InventoryMovements.Add(movement);
                await _db.SaveChangesAsync();

                adjustment.QuantityBefore = qtyBefore;
                adjustment.QuantityAfter = qtyAfter;
                adjustment.PalletsBefore = palletsBefore;
                adjustment.PalletsAfter = palletsAfter;
                
                adjustment.Status = InventoryAdjustmentStatus.APPROVED;
                adjustment.ApprovedBy = userId;
                adjustment.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                adjustment.MovementId = movement.MovementId;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return ApiResponse<bool>.SuccessResponse(true, "Adjustment approved and executed successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to approve adjustment request: {AdjustmentId}", adjustmentId);
                return ApiResponse<bool>.Failure($"Failed to approve adjustment: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> RejectAdjustmentAsync(Guid adjustmentId, string reason, Guid userId)
        {
            try
            {
                var adjustment = await _db.InventoryAdjustments
                    .FirstOrDefaultAsync(a => a.AdjustmentId == adjustmentId);

                if (adjustment == null)
                    return ApiResponse<bool>.Failure("Adjustment request not found.");

                if (adjustment.Status != InventoryAdjustmentStatus.PENDING_APPROVAL)
                    return ApiResponse<bool>.Failure($"Adjustment request is not pending approval. Current status: {adjustment.Status}");

                adjustment.Status = InventoryAdjustmentStatus.REJECTED;
                adjustment.ApprovedBy = userId;
                adjustment.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                adjustment.RejectionReason = reason?.Trim();

                await _db.SaveChangesAsync();

                return ApiResponse<bool>.SuccessResponse(true, "Adjustment request rejected successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reject adjustment request: {AdjustmentId}", adjustmentId);
                return ApiResponse<bool>.Failure($"Failed to reject adjustment: {ex.Message}");
            }
        }

        public async Task<ApiResponse<InventoryAdjustmentResponse>> GetAdjustmentByIdAsync(Guid adjustmentId)
        {
            try
            {
                var adjustment = await _db.InventoryAdjustments
                    .Include(a => a.Stock)
                    .ThenInclude(s => s.Location)
                    .FirstOrDefaultAsync(a => a.AdjustmentId == adjustmentId);

                if (adjustment == null)
                    return ApiResponse<InventoryAdjustmentResponse>.Failure("Adjustment not found.");

                var creatorUser = await _db.Users.FindAsync(adjustment.CreatedBy);
                var approverUser = adjustment.ApprovedBy.HasValue 
                    ? await _db.Users.FindAsync(adjustment.ApprovedBy.Value) 
                    : null;

                var response = new InventoryAdjustmentResponse
                {
                    AdjustmentId = adjustment.AdjustmentId,
                    StockId = adjustment.StockId,
                    ItemCode = adjustment.Stock.ItemCode,
                    ItemName = adjustment.Stock.ItemName,
                    BatchNumber = await _db.InventoryBatches
                        .Where(b => b.BatchId == adjustment.Stock.BatchId)
                        .Select(b => b.BatchNumber)
                        .FirstOrDefaultAsync() ?? "UNKNOWN",
                    LocationCode = adjustment.Stock.Location.LocationCode,
                    AdjustmentType = adjustment.AdjustmentType,
                    QuantityBefore = adjustment.QuantityBefore,
                    QuantityChanged = adjustment.QuantityChanged,
                    QuantityAfter = adjustment.QuantityAfter,
                    PalletsBefore = adjustment.PalletsBefore,
                    PalletsChanged = adjustment.PalletsChanged,
                    PalletsAfter = adjustment.PalletsAfter,
                    ReasonNotes = adjustment.ReasonNotes,
                    Status = adjustment.Status,
                    MovementId = adjustment.MovementId,
                    CreatedAt = adjustment.CreatedAt,
                    CreatedBy = adjustment.CreatedBy,
                    CreatedByUsername = creatorUser?.Username ?? "Unknown",
                    ApprovedBy = adjustment.ApprovedBy,
                    ApprovedByUsername = approverUser?.Username,
                    ApprovedAt = adjustment.ApprovedAt,
                    RejectionReason = adjustment.RejectionReason
                };

                return ApiResponse<InventoryAdjustmentResponse>.SuccessResponse(response, "Adjustment details retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve adjustment: {AdjustmentId}", adjustmentId);
                return ApiResponse<InventoryAdjustmentResponse>.Failure($"Failed to retrieve adjustment: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PagedResult<InventoryAdjustmentResponse>>> GetPagedAdjustmentsAsync(int pageNumber, int pageSize, InventoryAdjustmentStatus? status = null)
        {
            try
            {
                var query = _db.InventoryAdjustments
                    .Include(a => a.Stock)
                    .ThenInclude(s => s.Location)
                    .AsQueryable();

                if (status.HasValue)
                {
                    query = query.Where(a => a.Status == status.Value);
                }

                int totalCount = await query.CountAsync();
                var items = await query
                    .OrderByDescending(a => a.CreatedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var userIds = items.Select(a => a.CreatedBy)
                    .Concat(items.Where(a => a.ApprovedBy.HasValue).Select(a => a.ApprovedBy!.Value))
                    .Distinct()
                    .ToList();

                var usersMap = await _db.Users
                    .Where(u => userIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId, u => u.Username);

                var batchIds = items.Select(a => a.Stock.BatchId).Distinct().ToList();
                var batchesMap = await _db.InventoryBatches
                    .Where(b => batchIds.Contains(b.BatchId))
                    .ToDictionaryAsync(b => b.BatchId, b => b.BatchNumber);

                var responseList = items.Select(adjustment => new InventoryAdjustmentResponse
                {
                    AdjustmentId = adjustment.AdjustmentId,
                    StockId = adjustment.StockId,
                    ItemCode = adjustment.Stock.ItemCode,
                    ItemName = adjustment.Stock.ItemName,
                    BatchNumber = batchesMap.TryGetValue(adjustment.Stock.BatchId, out var batchNo) ? batchNo : "UNKNOWN",
                    LocationCode = adjustment.Stock.Location.LocationCode,
                    AdjustmentType = adjustment.AdjustmentType,
                    QuantityBefore = adjustment.QuantityBefore,
                    QuantityChanged = adjustment.QuantityChanged,
                    QuantityAfter = adjustment.QuantityAfter,
                    PalletsBefore = adjustment.PalletsBefore,
                    PalletsChanged = adjustment.PalletsChanged,
                    PalletsAfter = adjustment.PalletsAfter,
                    ReasonNotes = adjustment.ReasonNotes,
                    Status = adjustment.Status,
                    MovementId = adjustment.MovementId,
                    CreatedAt = adjustment.CreatedAt,
                    CreatedBy = adjustment.CreatedBy,
                    CreatedByUsername = usersMap.TryGetValue(adjustment.CreatedBy, out var cUser) ? cUser : "Unknown",
                    ApprovedBy = adjustment.ApprovedBy,
                    ApprovedByUsername = adjustment.ApprovedBy.HasValue && usersMap.TryGetValue(adjustment.ApprovedBy.Value, out var aUser) ? aUser : null,
                    ApprovedAt = adjustment.ApprovedAt,
                    RejectionReason = adjustment.RejectionReason
                }).ToList();

                var pagedResult = PagedResult<InventoryAdjustmentResponse>.Create(responseList, totalCount, pageNumber, pageSize);
                return ApiResponse<PagedResult<InventoryAdjustmentResponse>>.SuccessResponse(pagedResult, "Paged adjustments retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve paged adjustments.");
                return ApiResponse<PagedResult<InventoryAdjustmentResponse>>.Failure($"Failed to retrieve adjustments: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PagedResult<AvailableStockResponse>>> GetAvailableStockAsync(int pageNumber, int pageSize, string? itemCode = null)
        {
            try
            {
                var query = _db.InventoryStocks
                    .Include(s => s.Location)
                    .Include(s => s.Batch)
                    .Where(s => s.Status == "AVAILABLE" && (s.QuantityOnHand - s.QuantityAllocated) > 0);

                if (!string.IsNullOrWhiteSpace(itemCode))
                {
                    var cleanItemCode = itemCode.Trim();
                    query = query.Where(s => s.ItemCode == cleanItemCode);
                }

                // FEFO Order: Batch.ExpiryDate ASC, then InboundDate ASC (FIFO tie-breaker)
                query = query.OrderBy(s => s.Batch.ExpiryDate)
                             .ThenBy(s => s.InboundDate);

                int totalCount = await query.CountAsync();
                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(s => new AvailableStockResponse
                    {
                        StockId = s.StockId,
                        ItemCode = s.ItemCode,
                        ItemName = s.ItemName,
                        Unit = s.Unit,
                        LocationId = s.LocationId,
                        LocationCode = s.Location.LocationCode,
                        BatchId = s.BatchId,
                        BatchNumber = s.Batch.BatchNumber,
                        ExpiryDate = s.Batch.ExpiryDate,
                        InboundDate = s.InboundDate,
                        QuantityOnHand = s.QuantityOnHand,
                        QuantityAllocated = s.QuantityAllocated
                    })
                    .ToListAsync();

                var pagedResult = PagedResult<AvailableStockResponse>.Create(items, totalCount, pageNumber, pageSize);
                return ApiResponse<PagedResult<AvailableStockResponse>>.SuccessResponse(pagedResult, "Available stock retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve available stock. ItemCode: {ItemCode}", itemCode);
                return ApiResponse<PagedResult<AvailableStockResponse>>.Failure($"Failed to retrieve available stock: {ex.Message}");
            }
        }

        public async Task<ApiResponse<AllocationResultResponse>> AllocateStockAsync(AllocateInventoryRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<AllocationResultResponse>.Failure("Request is null");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            int maxRetries = 5;
            int delayMs = 100;

            for (int retry = 1; retry <= maxRetries; retry++)
            {
                using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
                try
                {
                    var result = new AllocationResultResponse
                    {
                        ReferenceDocumentId = request.ReferenceDocumentId
                    };

                    foreach (var itemRequest in request.Items)
                    {
                        var itemDetail = new AllocatedItemDetailDto
                        {
                            ItemCode = itemRequest.ItemCode,
                            RequestedQuantity = itemRequest.Quantity
                        };

                        // Fetch available stock records sorted by FEFO + FIFO tie-breaker, excluding expired batches
                        var stocks = await _db.InventoryStocks
                            .Include(s => s.Location)
                            .Include(s => s.Batch)
                            .Where(s => s.ItemCode == itemRequest.ItemCode 
                                        && s.Status == "AVAILABLE" 
                                        && (s.QuantityOnHand - s.QuantityAllocated) > 0
                                        && s.Batch.ExpiryDate > today)
                            .OrderBy(s => s.Batch.ExpiryDate)
                            .ThenBy(s => s.InboundDate)
                            .ToListAsync();

                        decimal totalAvailable = stocks.Sum(s => s.QuantityOnHand - s.QuantityAllocated);
                        if (totalAvailable < itemRequest.Quantity)
                        {
                            await transaction.RollbackAsync();
                            return ApiResponse<AllocationResultResponse>.Failure($"Insufficient inventory for item '{itemRequest.ItemCode}'. Available: {totalAvailable}, Requested: {itemRequest.Quantity}");
                        }

                        decimal remainingToAllocate = itemRequest.Quantity;
                        foreach (var stock in stocks)
                        {
                            decimal stockAvailable = stock.QuantityOnHand - stock.QuantityAllocated;
                            if (stockAvailable <= 0) continue;

                            decimal toAllocate = Math.Min(remainingToAllocate, stockAvailable);
                            
                            stock.QuantityAllocated += toAllocate;
                            stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                            stock.UpdatedBy = userId;

                            // Create inventory allocation record
                            var allocation = new InventoryAllocation
                            {
                                AllocationId = Guid.NewGuid(),
                                ReferenceDocumentId = request.ReferenceDocumentId,
                                StockId = stock.StockId,
                                AllocatedQuantity = toAllocate,
                                Status = "ALLOCATED",
                                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                                CreatedBy = userId
                            };
                            _db.InventoryAllocations.Add(allocation);

                            // Create movement ledger entry
                            var movement = new InventoryMovement
                            {
                                MovementId = Guid.NewGuid(),
                                StockId = stock.StockId,
                                ItemCode = itemRequest.ItemCode,
                                BatchId = stock.BatchId,
                                MovementType = "ALLOCATION",
                                Quantity = toAllocate,
                                FromLocationId = stock.LocationId,
                                ToLocationId = null,
                                ReferenceDocumentId = request.ReferenceDocumentId,
                                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                                CreatedBy = userId
                            };
                            _db.InventoryMovements.Add(movement);

                            itemDetail.Allocations.Add(new AllocatedBatchDetailDto
                            {
                                StockId = stock.StockId,
                                BatchId = stock.BatchId,
                                BatchNumber = stock.Batch.BatchNumber,
                                LocationId = stock.LocationId,
                                LocationCode = stock.Location.LocationCode,
                                AllocatedQuantity = toAllocate
                            });

                            remainingToAllocate -= toAllocate;
                            if (remainingToAllocate == 0) break;
                        }

                        result.Items.Add(itemDetail);
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Allocated stock atomically for ReferenceDocumentId: {ReferenceDocumentId}", request.ReferenceDocumentId);
                    return ApiResponse<AllocationResultResponse>.SuccessResponse(result, "Inventory allocated successfully.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _db.ChangeTracker.Clear();

                    bool isSerializationFailure = false;
                    var currentEx = ex;
                    while (currentEx != null)
                    {
                        var typeName = currentEx.GetType().Name;
                        if (typeName == "NpgsqlException" || typeName == "PostgresException")
                        {
                            var sqlStateProp = currentEx.GetType().GetProperty("SqlState");
                            var sqlState = sqlStateProp?.GetValue(currentEx) as string;
                            if (sqlState == "40001")
                            {
                                isSerializationFailure = true;
                                break;
                            }
                        }
                        currentEx = currentEx.InnerException;
                    }

                    if (isSerializationFailure && retry < maxRetries)
                    {
                        _logger.LogWarning("Serialization failure (40001) occurred during stock allocation. Retrying {Retry}/{MaxRetries}...", retry, maxRetries);
                        await Task.Delay(delayMs * retry);
                        continue;
                    }

                    _logger.LogError(ex, "Allocation failed for ReferenceDocumentId: {ReferenceDocumentId}", request.ReferenceDocumentId);
                    return ApiResponse<AllocationResultResponse>.Failure($"Allocation failed: {ex.Message}");
                }
            }

            return ApiResponse<AllocationResultResponse>.Failure("Allocation failed due to persistent serialization conflicts. Please try again.");
        }

        public async Task<ApiResponse<bool>> ReleaseAllocationAsync(ReleaseAllocationRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<bool>.Failure("Request is null");

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var allocations = await _db.InventoryAllocations
                    .Include(a => a.Stock)
                    .Where(a => a.ReferenceDocumentId == request.ReferenceDocumentId && a.Status == "ALLOCATED")
                    .ToListAsync();

                if (!allocations.Any())
                {
                    return ApiResponse<bool>.Failure("No active allocations found for the specified reference document.");
                }

                foreach (var allocation in allocations)
                {
                    var stock = allocation.Stock;
                    
                    // Deduct from Allocated Quantity
                    stock.QuantityAllocated -= allocation.AllocatedQuantity;
                    if (stock.QuantityAllocated < 0) stock.QuantityAllocated = 0;
                    
                    stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    stock.UpdatedBy = userId;

                    // Mark allocation as released
                    allocation.Status = "RELEASED";

                    // Log ledger movement
                    var movement = new InventoryMovement
                    {
                        MovementId = Guid.NewGuid(),
                        StockId = stock.StockId,
                        ItemCode = stock.ItemCode,
                        BatchId = stock.BatchId,
                        MovementType = "RELEASE_ALLOCATION",
                        Quantity = allocation.AllocatedQuantity,
                        FromLocationId = null,
                        ToLocationId = stock.LocationId,
                        ReferenceDocumentId = request.ReferenceDocumentId,
                        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                        CreatedBy = userId
                    };
                    _db.InventoryMovements.Add(movement);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Released allocations for ReferenceDocumentId: {ReferenceDocumentId}", request.ReferenceDocumentId);
                return ApiResponse<bool>.SuccessResponse(true, "Allocations released successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to release allocations for ReferenceDocumentId: {ReferenceDocumentId}", request.ReferenceDocumentId);
                return ApiResponse<bool>.Failure($"Failed to release allocation: {ex.Message}");
            }
        }

        public async Task<ApiResponse<List<PutawaySuggestionResponse>>> GetPutawaySuggestionsAsync(Guid stockId)
        {
            try
            {
                var stock = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .ThenInclude(l => l.Zone)
                    .Include(s => s.Batch)
                    .FirstOrDefaultAsync(s => s.StockId == stockId);

                if (stock == null)
                {
                    return ApiResponse<List<PutawaySuggestionResponse>>.Failure("Stock record not found.");
                }

                var warehouseId = stock.Location.Zone.WarehouseId;

                // Query all active locations in the same warehouse, excluding receiving or shipping zones, and the stock's current location.
                var locations = await _db.WarehouseLocations
                    .Include(l => l.Zone)
                    .Where(l => l.Zone.WarehouseId == warehouseId
                                && l.Status == "ACTIVE"
                                && l.Zone.Status == "ACTIVE"
                                && l.Zone.ZoneType != "RECEIVING"
                                && l.Zone.ZoneType != "SHIPPING"
                                && l.LocationId != stock.LocationId)
                    .ToListAsync();

                var suggestions = new List<PutawaySuggestionResponse>();

                // Get all active stock records in non-receiving zones of this warehouse to check for same item / same batch consolidation
                var activeStocks = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .Include(s => s.Batch)
                    .Where(s => s.Location.Zone.WarehouseId == warehouseId
                                && s.QuantityOnHand > 0
                                && s.Location.Zone.ZoneType != "RECEIVING"
                                && s.Location.Zone.ZoneType != "SHIPPING")
                    .ToListAsync();

                foreach (var loc in locations)
                {
                    // 1. Temperature compatibility
                    if (stock.RequiredTempMin.HasValue && loc.Zone.TemperatureMax.HasValue && stock.RequiredTempMin.Value > loc.Zone.TemperatureMax.Value)
                    {
                        continue;
                    }

                    if (stock.RequiredTempMax.HasValue && loc.Zone.TemperatureMin.HasValue && stock.RequiredTempMax.Value < loc.Zone.TemperatureMin.Value)
                    {
                        continue;
                    }

                    // 2. Capacity validation
                    int palletNeeded = Math.Max(1, stock.PalletCount);
                    if (loc.CurrentPallets + palletNeeded > loc.MaxCapacityPallets)
                    {
                        continue;
                    }

                    if (loc.Zone.CurrentPallets + palletNeeded > loc.Zone.MaxCapacityPallets)
                    {
                        continue;
                    }

                    // 3. Consolidation evaluation
                    var locStocks = activeStocks.Where(s => s.LocationId == loc.LocationId).ToList();

                    bool hasSameBatch = locStocks.Any(s => s.ItemCode == stock.ItemCode && s.BatchId == stock.BatchId);
                    bool hasSameItem = locStocks.Any(s => s.ItemCode == stock.ItemCode);

                    int score = 0;
                    string matchType = "COMPATIBLE";

                    if (hasSameBatch)
                    {
                        score = 100;
                        matchType = "SAME_BATCH";
                    }
                    else if (hasSameItem)
                    {
                        score = 80;
                        matchType = "SAME_ITEM";
                    }
                    else if (loc.CurrentPallets == 0)
                    {
                        score = 50;
                        matchType = "EMPTY";
                    }
                    else
                    {
                        score = 20;
                        matchType = "COMPATIBLE";
                    }

                    suggestions.Add(new PutawaySuggestionResponse
                    {
                        LocationId = loc.LocationId,
                        LocationCode = loc.LocationCode,
                        ZoneCode = loc.Zone.ZoneCode,
                        CurrentPallets = loc.CurrentPallets,
                        MaxCapacityPallets = loc.MaxCapacityPallets,
                        RemainingCapacity = loc.MaxCapacityPallets - loc.CurrentPallets,
                        SuitabilityScore = score,
                        MatchType = matchType
                    });
                }

                // Rank suggestions: MatchType Score desc, RemainingCapacity desc, LocationCode asc
                var orderedSuggestions = suggestions
                    .OrderByDescending(s => s.SuitabilityScore)
                    .ThenByDescending(s => s.RemainingCapacity)
                    .ThenBy(s => s.LocationCode)
                    .ToList();

                return ApiResponse<List<PutawaySuggestionResponse>>.SuccessResponse(orderedSuggestions, "Putaway suggestions retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get putaway suggestions for stock: {StockId}", stockId);
                return ApiResponse<List<PutawaySuggestionResponse>>.Failure($"Failed to get putaway suggestions: {ex.Message}");
            }
        }

        public async Task<ApiResponse<List<StockPutawaySuggestionsResponse>>> GetPutawaySuggestionsByReceiptAsync(Guid receiptId)
        {
            try
            {
                var receipt = await _db.WarehouseReceipts.FindAsync(receiptId);
                if (receipt == null)
                {
                    return ApiResponse<List<StockPutawaySuggestionsResponse>>.Failure("Warehouse receipt not found.");
                }

                var stockIds = await _db.InventoryMovements
                    .Where(m => m.ReferenceDocumentId == receiptId && m.MovementType == "INBOUND")
                    .Select(m => m.StockId)
                    .Distinct()
                    .ToListAsync();

                if (!stockIds.Any())
                {
                    return ApiResponse<List<StockPutawaySuggestionsResponse>>.SuccessResponse(new List<StockPutawaySuggestionsResponse>(), "No inbound stocks found for this receipt.");
                }

                var stocks = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .ThenInclude(l => l.Zone)
                    .Include(s => s.Batch)
                    .Where(s => stockIds.Contains(s.StockId))
                    .ToListAsync();

                var result = new List<StockPutawaySuggestionsResponse>();

                foreach (var stock in stocks)
                {
                    var stockResult = await GetPutawaySuggestionsAsync(stock.StockId);
                    if (stockResult.Success && stockResult.Data != null)
                    {
                        result.Add(new StockPutawaySuggestionsResponse
                        {
                            StockId = stock.StockId,
                            ItemCode = stock.ItemCode,
                            BatchNumber = stock.Batch?.BatchNumber ?? "",
                            PalletCount = stock.PalletCount,
                            Suggestions = stockResult.Data
                        });
                    }
                }

                return ApiResponse<List<StockPutawaySuggestionsResponse>>.SuccessResponse(result, "Receipt putaway suggestions retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get putaway suggestions for receipt: {ReceiptId}", receiptId);
                return ApiResponse<List<StockPutawaySuggestionsResponse>>.Failure($"Failed to get putaway suggestions: {ex.Message}");
            }
        }
    }
}
