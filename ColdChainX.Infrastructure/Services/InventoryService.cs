using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Infrastructure.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(ApplicationDbContext db, ILogger<InventoryService> logger)
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
                // 1. Fetch Source Stock with Batch to verify details
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

                // 2. Fetch Destination Location & Zone
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

                // 3. Validation: Destination Location Capacity Check
                if (destLocation.CurrentPallets + request.Pallets > destLocation.MaxCapacityPallets)
                {
                    return ApiResponse<bool>.Failure($"Capacity exceeded: Destination location '{destLocation.LocationCode}' does not have enough capacity. Current: {destLocation.CurrentPallets}, Adding: {request.Pallets}, Max: {destLocation.MaxCapacityPallets}.");
                }

                // 4. Validation: Destination Zone Capacity Check
                if (destZone.CurrentPallets + request.Pallets > destZone.MaxCapacityPallets)
                {
                    return ApiResponse<bool>.Failure($"Capacity exceeded: Destination zone '{destZone.ZoneCode}' does not have enough capacity. Current: {destZone.CurrentPallets}, Adding: {request.Pallets}, Max: {destZone.MaxCapacityPallets}.");
                }

                // 5. Validation: Destination Zone Temperature Compatibility Check
                // Read pre-populated RequiredTempMin and RequiredTempMax directly from stock. Do not parse TempCondition.
                if (sourceStock.RequiredTempMin.HasValue && destZone.TemperatureMax.HasValue && sourceStock.RequiredTempMin.Value > destZone.TemperatureMax.Value)
                {
                    return ApiResponse<bool>.Failure($"Temperature incompatible: Stock requires min temp {sourceStock.RequiredTempMin.Value}°C, but destination zone max temp is {destZone.TemperatureMax.Value}°C.");
                }

                if (sourceStock.RequiredTempMax.HasValue && destZone.TemperatureMin.HasValue && sourceStock.RequiredTempMax.Value < destZone.TemperatureMin.Value)
                {
                    return ApiResponse<bool>.Failure($"Temperature incompatible: Stock requires max temp {sourceStock.RequiredTempMax.Value}°C, but destination zone min temp is {destZone.TemperatureMin.Value}°C.");
                }

                // 6. Deduct Quantity and Pallets from Source Stock
                var sourceZone = sourceStock.Location.Zone;
                
                sourceStock.QuantityOnHand -= request.Quantity;
                sourceStock.PalletCount -= request.Pallets;
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
                destLocation.CurrentPallets += request.Pallets;
                destZone.CurrentPallets += request.Pallets;

                sourceStock.Location.CurrentPallets -= request.Pallets;
                if (sourceStock.Location.CurrentPallets < 0)
                    sourceStock.Location.CurrentPallets = 0;

                if (sourceZone != null)
                {
                    sourceZone.CurrentPallets -= request.Pallets;
                    if (sourceZone.CurrentPallets < 0)
                        sourceZone.CurrentPallets = 0;
                }

                // 7. Find or Create Destination Stock (Status = "AVAILABLE")
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
                        PalletCount = request.Pallets,
                        RequiredTempMin = sourceStock.RequiredTempMin,
                        RequiredTempMax = sourceStock.RequiredTempMax
                    };
                    _db.InventoryStocks.Add(destStock);
                }
                else
                {
                    destStock.QuantityOnHand += request.Quantity;
                    destStock.PalletCount += request.Pallets;
                    destStock.Status = "AVAILABLE"; // Reactivate if it was INACTIVE
                    destStock.RequiredTempMin = sourceStock.RequiredTempMin;
                    destStock.RequiredTempMax = sourceStock.RequiredTempMax;
                    destStock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    destStock.UpdatedBy = userId;
                }

                // 8. Log InventoryMovement
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

        public async Task<ApiResponse<bool>> AdjustStockAsync(InventoryAdjustmentRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<bool>.Failure("Request is null");

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

                // 2. Calculate Qty Delta and Pallet Delta
                decimal deltaQty = request.IsAbsoluteCount
                    ? request.Quantity - stock.QuantityOnHand
                    : request.Quantity;

                int deltaPallets = request.IsAbsoluteCount
                    ? request.Pallets - stock.PalletCount
                    : request.Pallets;

                // 3. Validation: Quantity and Pallet Count must not become negative
                if (stock.QuantityOnHand + deltaQty < 0)
                {
                    return ApiResponse<bool>.Failure($"Adjustment failed: Resulting quantity cannot be negative. Current: {stock.QuantityOnHand}, Delta: {deltaQty}.");
                }

                if (stock.PalletCount + deltaPallets < 0)
                {
                    return ApiResponse<bool>.Failure($"Adjustment failed: Resulting pallet count cannot be negative. Current: {stock.PalletCount}, Delta: {deltaPallets}.");
                }

                // 4. Validation: Location and Zone Capacity checks when adding pallets
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

                // 5. Apply changes and save state snapshots
                decimal qtyBefore = stock.QuantityOnHand;
                stock.QuantityOnHand += deltaQty;
                decimal qtyAfter = stock.QuantityOnHand;

                stock.PalletCount += deltaPallets;
                stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                stock.UpdatedBy = userId;

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

                // 6. Log InventoryMovement
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

                // 7. Log InventoryAdjustment
                var adjustment = new InventoryAdjustment
                {
                    AdjustmentId = Guid.NewGuid(),
                    StockId = stock.StockId,
                    AdjustmentType = request.AdjustmentType,
                    QuantityBefore = qtyBefore,
                    QuantityChanged = deltaQty,
                    QuantityAfter = qtyAfter,
                    ReasonNotes = request.Reason.Trim(),
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    CreatedBy = userId,
                    MovementId = movement.MovementId
                };
                _db.InventoryAdjustments.Add(adjustment);

                await _db.SaveChangesAsync();
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.CommitAsync();
                }

                _logger.LogInformation("Stock adjusted successfully. StockId: {StockId}, AdjustmentType: {AdjustmentType}, QtyChanged: {QtyChanged}, PalletsChanged: {PalletsChanged}",
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
                _logger.LogError(ex, "Allocation failed for ReferenceDocumentId: {ReferenceDocumentId}", request.ReferenceDocumentId);
                return ApiResponse<AllocationResultResponse>.Failure($"Allocation failed: {ex.Message}");
            }
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
    }
}
