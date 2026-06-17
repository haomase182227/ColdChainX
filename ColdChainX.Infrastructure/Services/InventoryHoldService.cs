using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Shared.Responses;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using ColdChainX.Core.Enums;
using ColdChainX.Application.DTOs.WarehouseReceipt;

namespace ColdChainX.Infrastructure.Services
{
    public class InventoryHoldService : IInventoryHoldService
    {
        private readonly IInventoryHoldRepository _holdRepo;
        private readonly ApplicationDbContext _db;
        private readonly IInventoryService _inventoryService;
        private readonly ILogger<InventoryHoldService> _logger;

        public InventoryHoldService(
            IInventoryHoldRepository holdRepo, 
            ApplicationDbContext db, 
            IInventoryService inventoryService, 
            ILogger<InventoryHoldService> logger)
        {
            _holdRepo = holdRepo;
            _db = db;
            _inventoryService = inventoryService;
            _logger = logger;
        }

        public async Task<ApiResponse<HoldResponseDto>> CreateHoldAsync(CreateInventoryHoldDto dto, Guid userId)
        {
            if (dto == null)
                return ApiResponse<HoldResponseDto>.Failure("Request data is null.");

            try
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    var isOuterTransaction = _db.Database.CurrentTransaction == null;
                    using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
                    try
                    {
                        var stock = await _db.InventoryStocks
                            .Include(s => s.Location)
                            .FirstOrDefaultAsync(s => s.StockId == dto.StockId);

                        if (stock == null)
                            return ApiResponse<HoldResponseDto>.Failure("Stock record not found.");

                        decimal availableQty = stock.QuantityOnHand - stock.QuantityAllocated;
                        if (availableQty < dto.Quantity)
                            return ApiResponse<HoldResponseDto>.Failure($"Insufficient available quantity to place on hold. Available: {availableQty}, Requested: {dto.Quantity}");

                        Guid targetStockId = stock.StockId;

                        // Handle Partial Hold using Quarantine Relocation
                        if (dto.Quantity < stock.QuantityOnHand)
                        {
                            if (!dto.TargetQuarantineLocationId.HasValue)
                                return ApiResponse<HoldResponseDto>.Failure("Target Quarantine Location ID is required for partial stock holds.");

                            int palletsToMove = 0;
                            if (stock.QuantityOnHand > 0 && stock.PalletCount > 0)
                            {
                                palletsToMove = (int)Math.Ceiling(dto.Quantity * stock.PalletCount / stock.QuantityOnHand);
                                if (palletsToMove < 1 && dto.Quantity > 0)
                                {
                                    palletsToMove = 1;
                                }
                                if (palletsToMove > stock.PalletCount)
                                {
                                    palletsToMove = stock.PalletCount;
                                }
                            }
                            else
                            {
                                palletsToMove = 0;
                            }

                            var relocateRequest = new StockRelocationRequest
                            {
                                SourceLocationId = stock.LocationId,
                                DestinationLocationId = dto.TargetQuarantineLocationId.Value,
                                ItemCode = stock.ItemCode,
                                BatchId = stock.BatchId,
                                Quantity = dto.Quantity,
                                Pallets = palletsToMove
                            };

                            var relocateResult = await _inventoryService.RelocateStockAsync(relocateRequest, userId);
                            if (!relocateResult.Success)
                            {
                                return ApiResponse<HoldResponseDto>.Failure($"Quarantine relocation failed: {relocateResult.Message}");
                            }

                            // Fetch the newly created stock record in quarantine
                            var quarantinedStock = await _db.InventoryStocks
                                .Include(s => s.Location)
                                .FirstOrDefaultAsync(s => s.LocationId == dto.TargetQuarantineLocationId.Value 
                                                          && s.ItemCode == stock.ItemCode 
                                                          && s.BatchId == stock.BatchId);

                            if (quarantinedStock == null)
                                return ApiResponse<HoldResponseDto>.Failure("Failed to resolve quarantined stock entity.");

                            targetStockId = quarantinedStock.StockId;
                            stock = quarantinedStock;
                        }

                        // Change status of target stock record to HOLD
                        stock.Status = "HOLD";
                        stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        stock.UpdatedBy = userId;

                        var hold = new InventoryHold
                        {
                            HoldId = Guid.NewGuid(),
                            StockId = targetStockId,
                            HoldQuantity = dto.Quantity,
                            ReasonCode = dto.ReasonCode,
                            Notes = dto.Notes,
                            Status = "HOLD",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            CreatedBy = userId
                        };

                        await _holdRepo.AddAsync(hold);
                        await _holdRepo.SaveChangesAsync();
                        if (isOuterTransaction && transaction != null)
                        {
                            await transaction.CommitAsync();
                        }

                        _logger.LogInformation("Stock placed on hold. StockId: {StockId}, Qty: {Qty}, Reason: {Reason}", targetStockId, dto.Quantity, dto.ReasonCode);

                        var response = new HoldResponseDto
                        {
                            HoldId = hold.HoldId,
                            StockId = hold.StockId,
                            ItemCode = stock.ItemCode,
                            ItemName = stock.ItemName,
                            LocationCode = stock.Location?.LocationCode ?? "UNKNOWN",
                            Quantity = hold.HoldQuantity,
                            ReasonCode = hold.ReasonCode,
                            Status = hold.Status,
                            CreatedAt = hold.CreatedAt,
                            CreatedBy = hold.CreatedBy
                        };

                        return ApiResponse<HoldResponseDto>.SuccessResponse(response, "Stock successfully placed on hold.");
                    }
                    catch
                    {
                        if (isOuterTransaction && transaction != null)
                        {
                            await transaction.RollbackAsync();
                        }
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to place stock on hold. StockId: {StockId}", dto.StockId);
                return ApiResponse<HoldResponseDto>.Failure($"Failed to create hold: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> ReleaseHoldAsync(Guid holdId, ReleaseInventoryHoldDto dto, Guid userId)
        {
            if (dto == null)
                return ApiResponse<bool>.Failure("Request data is null.");

            try
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    var isOuterTransaction = _db.Database.CurrentTransaction == null;
                    using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
                    try
                    {
                        var hold = await _holdRepo.GetByIdAsync(holdId);
                        if (hold == null)
                            return ApiResponse<bool>.Failure("Hold record not found.");

                        if (hold.Status != "HOLD")
                            return ApiResponse<bool>.Failure($"Hold is already in '{hold.Status}' status.");

                        var stock = hold.Stock;
                        if (stock == null)
                            return ApiResponse<bool>.Failure("Held stock record not found.");

                        // Revert Status back to AVAILABLE
                        stock.Status = "AVAILABLE";
                        stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        stock.UpdatedBy = userId;

                        // Handle Optional Release Relocation (moving stock out of quarantine zone)
                        if (dto.TargetReleaseLocationId.HasValue && dto.TargetReleaseLocationId.Value != stock.LocationId)
                        {
                            var relocateRequest = new StockRelocationRequest
                            {
                                SourceLocationId = stock.LocationId,
                                DestinationLocationId = dto.TargetReleaseLocationId.Value,
                                ItemCode = stock.ItemCode,
                                BatchId = stock.BatchId,
                                Quantity = hold.HoldQuantity,
                                Pallets = stock.PalletCount
                            };

                            // Temporarily save back to database so relocation works (relocation checks for status = AVAILABLE)
                            await _db.SaveChangesAsync();

                            var relocateResult = await _inventoryService.RelocateStockAsync(relocateRequest, userId);
                            if (!relocateResult.Success)
                            {
                                return ApiResponse<bool>.Failure($"Release relocation failed: {relocateResult.Message}");
                            }
                        }

                        hold.Status = "RELEASED";
                        hold.ReleasedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        hold.ReleasedBy = userId;
                        hold.ReleaseNotes = dto.ReleaseNotes;

                        await _holdRepo.UpdateAsync(hold);
                        await _holdRepo.SaveChangesAsync();
                        if (isOuterTransaction && transaction != null)
                        {
                            await transaction.CommitAsync();
                        }

                        _logger.LogInformation("Hold released successfully. HoldId: {HoldId}, StockId: {StockId}", holdId, hold.StockId);
                        return ApiResponse<bool>.SuccessResponse(true, "Hold released successfully.");
                    }
                    catch
                    {
                        if (isOuterTransaction && transaction != null)
                        {
                            await transaction.RollbackAsync();
                        }
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release hold {HoldId}", holdId);
                return ApiResponse<bool>.Failure($"Failed to release hold: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PagedResult<HoldResponseDto>>> GetPagedHoldsAsync(int pageNumber, int pageSize, string? status, string? reasonCode, string? itemCode)
        {
            try
            {
                var holds = await _holdRepo.GetPagedHoldsAsync(pageNumber, pageSize, status, reasonCode, itemCode);
                var total = await _holdRepo.CountHoldsAsync(status, reasonCode, itemCode);

                var dtos = holds.Select(h => new HoldResponseDto
                {
                    HoldId = h.HoldId,
                    StockId = h.StockId,
                    ItemCode = h.Stock.ItemCode,
                    ItemName = h.Stock.ItemName,
                    LocationCode = h.Stock.Location?.LocationCode ?? "UNKNOWN",
                    Quantity = h.HoldQuantity,
                    ReasonCode = h.ReasonCode,
                    Status = h.Status,
                    CreatedAt = h.CreatedAt,
                    CreatedBy = h.CreatedBy,
                    ReleasedAt = h.ReleasedAt,
                    ReleasedBy = h.ReleasedBy,
                    ReleaseNotes = h.ReleaseNotes
                }).ToList();

                var pagedResult = PagedResult<HoldResponseDto>.Create(dtos, total, pageNumber, pageSize);
                return ApiResponse<PagedResult<HoldResponseDto>>.SuccessResponse(pagedResult, "Holds retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve holds.");
                return ApiResponse<PagedResult<HoldResponseDto>>.Failure($"Failed to fetch holds: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> AdjustOutHoldAsync(Guid holdId, string reasonNotes, Guid userId)
        {
            try
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    var isOuterTransaction = _db.Database.CurrentTransaction == null;
                    using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
                    try
                    {
                        var hold = await _holdRepo.GetByIdAsync(holdId);
                        if (hold == null)
                            return ApiResponse<bool>.Failure("Hold record not found.");

                        if (hold.Status != "HOLD")
                            return ApiResponse<bool>.Failure($"Cannot adjust out hold in '{hold.Status}' status.");

                        var stock = hold.Stock;
                        if (stock == null)
                            return ApiResponse<bool>.Failure("Stock record not found.");

                        // Trigger stock adjustment (reducing stock to 0)
                        var adjustRequest = new InventoryAdjustmentRequest
                        {
                            StockId = hold.StockId,
                            Quantity = -hold.HoldQuantity,
                            Pallets = -stock.PalletCount,
                            IsAbsoluteCount = false,
                            AdjustmentType = InventoryAdjustmentType.DAMAGED,
                            Reason = $"Adjusted out via Hold resolution: {reasonNotes}"
                        };

                        // Temporarily mark AVAILABLE so adjustment service accepts it
                        stock.Status = "AVAILABLE";
                        await _db.SaveChangesAsync();

                        var adjustResult = await _inventoryService.AdjustStockAsync(adjustRequest, userId, autoApprove: true);
                        if (!adjustResult.Success)
                        {
                            // Rollback status
                            stock.Status = "HOLD";
                            await _db.SaveChangesAsync();
                            return ApiResponse<bool>.Failure($"Adjustment failed: {adjustResult.Message}");
                        }

                        // Retrieve the latest logged adjustment to link it
                        var latestAdjustment = await _db.InventoryAdjustments
                            .Where(a => a.StockId == hold.StockId)
                            .OrderByDescending(a => a.CreatedAt)
                            .FirstOrDefaultAsync();

                        hold.Status = "ADJUSTED";
                        hold.ReleasedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        hold.ReleasedBy = userId;
                        hold.ReleaseNotes = $"Adjusted out. Variance posted.";
                        hold.AdjustmentId = latestAdjustment?.AdjustmentId;

                        await _holdRepo.UpdateAsync(hold);
                        await _holdRepo.SaveChangesAsync();
                        if (isOuterTransaction && transaction != null)
                        {
                            await transaction.CommitAsync();
                        }

                        _logger.LogInformation("Hold stock adjusted out. HoldId: {HoldId}, Quantity: {Qty}", holdId, hold.HoldQuantity);
                        return ApiResponse<bool>.SuccessResponse(true, "Hold stock adjusted out and removed from inventory successfully.");
                    }
                    catch
                    {
                        if (isOuterTransaction && transaction != null)
                        {
                            await transaction.RollbackAsync();
                        }
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to adjust out hold {HoldId}", holdId);
                return ApiResponse<bool>.Failure($"Failed to adjust out hold: {ex.Message}");
            }
        }

        public async Task<ApiResponse<HoldResponseDto>> GetHoldByIdAsync(Guid holdId)
        {
            try
            {
                var h = await _holdRepo.GetByIdAsync(holdId);
                if (h == null)
                    return ApiResponse<HoldResponseDto>.Failure("Hold record not found.");

                var dto = new HoldResponseDto
                {
                    HoldId = h.HoldId,
                    StockId = h.StockId,
                    ItemCode = h.Stock.ItemCode,
                    ItemName = h.Stock.ItemName,
                    LocationCode = h.Stock.Location?.LocationCode ?? "UNKNOWN",
                    Quantity = h.HoldQuantity,
                    ReasonCode = h.ReasonCode,
                    Status = h.Status,
                    CreatedAt = h.CreatedAt,
                    CreatedBy = h.CreatedBy,
                    ReleasedAt = h.ReleasedAt,
                    ReleasedBy = h.ReleasedBy,
                    ReleaseNotes = h.ReleaseNotes
                };

                return ApiResponse<HoldResponseDto>.SuccessResponse(dto, "Hold retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve hold {HoldId}", holdId);
                return ApiResponse<HoldResponseDto>.Failure($"Failed to retrieve hold: {ex.Message}");
            }
        }
    }
}
