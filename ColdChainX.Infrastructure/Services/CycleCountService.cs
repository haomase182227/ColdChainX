using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Core.Enums;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.CycleCount;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Infrastructure.Services
{
    public class CycleCountService : ICycleCountService
    {
        private readonly ICycleCountRepository _cycleCountRepo;
        private readonly ApplicationDbContext _db;
        private readonly IInventoryService _inventoryService;
        private readonly ILogger<CycleCountService> _logger;

        public CycleCountService(
            ICycleCountRepository cycleCountRepo,
            ApplicationDbContext db,
            IInventoryService inventoryService,
            ILogger<CycleCountService> logger)
        {
            _cycleCountRepo = cycleCountRepo;
            _db = db;
            _inventoryService = inventoryService;
            _logger = logger;
        }

        public async Task<ApiResponse<CycleCountPlanResponse>> CreatePlanAsync(CreateCycleCountPlanDto dto, Guid userId)
        {
            if (dto == null)
                return ApiResponse<CycleCountPlanResponse>.Failure("Request data is null.");

            var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null || (user.Role?.RoleName != "Manager" && user.Role?.RoleName != "Admin"))
            {
                return ApiResponse<CycleCountPlanResponse>.Failure("Only Managers or Admins can create cycle count plans.");
            }

            // Get target location IDs
            var locationIds = new HashSet<Guid>();
            if (dto.LocationIds != null)
            {
                foreach (var locId in dto.LocationIds) 
                    locationIds.Add(locId);
            }
            if (dto.ZoneIds != null)
            {
                var zoneLocs = await _db.WarehouseLocations
                    .Where(l => dto.ZoneIds.Contains(l.ZoneId) && l.Status == "ACTIVE")
                    .Select(l => l.LocationId)
                    .ToListAsync();
                foreach (var locId in zoneLocs) 
                    locationIds.Add(locId);
            }

            if (locationIds.Count == 0)
            {
                return ApiResponse<CycleCountPlanResponse>.Failure("No active locations found for the specified zones or locations.");
            }

            var plan = new CycleCountPlan
            {
                PlanId = Guid.NewGuid(),
                PlanCode = $"CC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..4].ToUpper()}",
                Status = dto.AssignedToUserId.HasValue ? CycleCountPlanStatus.ASSIGNED : CycleCountPlanStatus.DRAFT,
                WarehouseId = dto.WarehouseId,
                AssignedToUserId = dto.AssignedToUserId,
                Notes = dto.Notes,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                CreatedBy = userId
            };

            var entries = new List<CycleCountEntry>();
            foreach (var locId in locationIds)
            {
                var stocks = await _db.InventoryStocks
                    .Include(s => s.Location)
                    .Include(s => s.Batch)
                    .Where(s => s.LocationId == locId && s.Status != "INACTIVE")
                    .ToListAsync();

                if (stocks.Count == 0)
                {
                    // Location is expected to be empty
                    entries.Add(new CycleCountEntry
                    {
                        EntryId = Guid.NewGuid(),
                        PlanId = plan.PlanId,
                        LocationId = locId,
                        StockId = null,
                        ItemCode = "EMPTY",
                        BatchId = null,
                        SystemQuantity = 0,
                        SystemPallets = 0,
                        Status = CycleCountEntryStatus.PENDING
                    });
                }
                else
                {
                    foreach (var stock in stocks)
                    {
                        entries.Add(new CycleCountEntry
                        {
                            EntryId = Guid.NewGuid(),
                            PlanId = plan.PlanId,
                            LocationId = locId,
                            StockId = stock.StockId,
                            ItemCode = stock.ItemCode,
                            BatchId = stock.BatchId,
                            SystemQuantity = stock.QuantityOnHand,
                            SystemPallets = stock.PalletCount,
                            Status = CycleCountEntryStatus.PENDING
                        });
                    }
                }
            }

            plan.Entries = entries;
            await _cycleCountRepo.AddPlanAsync(plan);
            await _cycleCountRepo.SaveChangesAsync();

            return await GetPlanDetailsAsync(plan.PlanId, userId);
        }

        public async Task<ApiResponse<bool>> StartCountingAsync(Guid planId, Guid userId)
        {
            var plan = await _cycleCountRepo.GetPlanByIdAsync(planId);
            if (plan == null) 
                return ApiResponse<bool>.Failure("Cycle count plan not found.");

            if (plan.Status != CycleCountPlanStatus.ASSIGNED && plan.Status != CycleCountPlanStatus.DRAFT)
            {
                return ApiResponse<bool>.Failure($"Cannot start counting in plan status '{plan.Status}'.");
            }

            plan.Status = CycleCountPlanStatus.COUNTING;
            await _cycleCountRepo.UpdatePlanAsync(plan);
            await _cycleCountRepo.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Counting started.");
        }

        public async Task<ApiResponse<bool>> SubmitCountsAsync(Guid planId, SubmitCycleCountsDto dto, Guid userId)
        {
            if (dto == null)
                return ApiResponse<bool>.Failure("Submit counts data is null.");

            var plan = await _cycleCountRepo.GetPlanByIdAsync(planId);
            if (plan == null) 
                return ApiResponse<bool>.Failure("Cycle count plan not found.");

            if (plan.Status != CycleCountPlanStatus.COUNTING)
            {
                return ApiResponse<bool>.Failure("Plan is not in COUNTING status.");
            }

            var isOuterTransaction = _db.Database.CurrentTransaction == null;
            using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                var entries = plan.Entries.ToList();
                bool hasDiscrepancy = false;

                foreach (var countDto in dto.Counts)
                {
                    var entry = entries.FirstOrDefault(e => e.EntryId == countDto.EntryId);
                    if (entry == null) 
                        continue;

                    // If unexpected stock is found, capture item code and batch details
                    if (!string.IsNullOrWhiteSpace(countDto.FoundItemCode) && entry.ItemCode == "EMPTY")
                    {
                        entry.ItemCode = countDto.FoundItemCode.Trim();
                        entry.BatchId = countDto.FoundBatchId;
                    }

                    entry.CountedQuantity = countDto.CountedQuantity;
                    entry.CountedPallets = countDto.CountedPallets;
                    entry.CountedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    entry.CountedBy = userId;
                    entry.Status = CycleCountEntryStatus.COUNTED;

                    // Compute variance
                    entry.VarianceQuantity = countDto.CountedQuantity - entry.SystemQuantity;
                    entry.VariancePallets = countDto.CountedPallets - entry.SystemPallets;

                    if (entry.VarianceQuantity != 0 || entry.VariancePallets != 0)
                    {
                        hasDiscrepancy = true;
                    }
                    else
                    {
                        // Auto-approved since there is no variance
                        entry.Status = CycleCountEntryStatus.APPROVED;
                        entry.ReviewedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        entry.ReviewedBy = userId;
                    }

                    await _cycleCountRepo.UpdateEntryAsync(entry);
                }

                // Transition plan status
                bool allCountedOrApproved = entries.All(e => e.Status == CycleCountEntryStatus.COUNTED || e.Status == CycleCountEntryStatus.APPROVED);
                if (allCountedOrApproved)
                {
                    plan.Status = hasDiscrepancy ? CycleCountPlanStatus.AWAITING_APPROVAL : CycleCountPlanStatus.COMPLETED;
                    if (plan.Status == CycleCountPlanStatus.COMPLETED)
                    {
                        plan.CompletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        plan.CompletedBy = userId;
                    }
                }

                await _cycleCountRepo.UpdatePlanAsync(plan);
                await _cycleCountRepo.SaveChangesAsync();
                
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.CommitAsync();
                }

                _logger.LogInformation("Counts submitted for CycleCountPlan {PlanId} by User {UserId}", planId, userId);
                return ApiResponse<bool>.SuccessResponse(true, "Counts submitted successfully.");
            }
            catch (Exception ex)
            {
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                _logger.LogError(ex, "Failed to submit counts for plan {PlanId}", planId);
                return ApiResponse<bool>.Failure($"Failed to submit counts: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> ReviewVarianceAsync(Guid entryId, ReviewVarianceDto dto, Guid managerId)
        {
            if (dto == null)
                return ApiResponse<bool>.Failure("Review variance data is null.");

            var manager = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == managerId);
            if (manager == null || (manager.Role?.RoleName != "Manager" && manager.Role?.RoleName != "Admin"))
            {
                return ApiResponse<bool>.Failure("Only Managers or Admins can review variances.");
            }

            var entry = await _cycleCountRepo.GetEntryByIdAsync(entryId);
            if (entry == null) 
                return ApiResponse<bool>.Failure("Cycle count entry not found.");

            if (entry.Status != CycleCountEntryStatus.COUNTED)
            {
                return ApiResponse<bool>.Failure($"Entry is in '{entry.Status}' status and cannot be reviewed.");
            }

            var plan = entry.Plan;
            if (plan == null) 
                return ApiResponse<bool>.Failure("Cycle count plan associated with this entry not found.");

            var isOuterTransaction = _db.Database.CurrentTransaction == null;
            using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                entry.ReviewedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                entry.ReviewedBy = managerId;
                entry.ManagerNotes = dto.ManagerNotes?.Trim();

                if (dto.Approve)
                {
                    entry.Status = CycleCountEntryStatus.APPROVED;
                    Guid? targetStockId = entry.StockId;

                    if (targetStockId.HasValue)
                    {
                        // Update existing stock record using AdjustStockAsync
                        var adjustRequest = new InventoryAdjustmentRequest
                        {
                            StockId = targetStockId.Value,
                            Quantity = entry.VarianceQuantity ?? 0,
                            Pallets = entry.VariancePallets ?? 0,
                            IsAbsoluteCount = false,
                            AdjustmentType = InventoryAdjustmentType.CYCLE_COUNT,
                            Reason = $"Cycle Count Plan: {plan.PlanCode}, Entry: {entry.EntryId}. Notes: {dto.ManagerNotes}"
                        };

                        var adjustResult = await _inventoryService.AdjustStockAsync(adjustRequest, managerId, autoApprove: true);
                        if (!adjustResult.Success)
                        {
                            throw new Exception($"Stock adjustment failed: {adjustResult.Message}");
                        }

                        var latestAdjustment = await _db.InventoryAdjustments
                            .Where(a => a.StockId == targetStockId.Value)
                            .OrderByDescending(a => a.CreatedAt)
                            .FirstOrDefaultAsync();

                        if (latestAdjustment != null)
                        {
                            entry.AdjustmentId = latestAdjustment.AdjustmentId;
                        }
                    }
                    else
                    {
                        // Picker found unexpected stock. We need to create a new stock record!
                        if (entry.ItemCode == "EMPTY" || string.IsNullOrWhiteSpace(entry.ItemCode))
                        {
                            throw new Exception("Cannot approve count without a valid ItemCode.");
                        }

                        var itemRef = await _db.InventoryStocks
                            .FirstOrDefaultAsync(s => s.ItemCode == entry.ItemCode);

                        if (itemRef == null)
                        {
                            throw new Exception($"Cannot find any reference in the system for ItemCode '{entry.ItemCode}' to resolve customer and unit details.");
                        }

                        // Validate batch if specified
                        Guid resolvedBatchId;
                        if (entry.BatchId.HasValue)
                        {
                            var batchExists = await _db.InventoryBatches.AnyAsync(b => b.BatchId == entry.BatchId.Value);
                            if (!batchExists)
                            {
                                throw new Exception($"Batch ID '{entry.BatchId.Value}' does not exist.");
                            }
                            resolvedBatchId = entry.BatchId.Value;
                        }
                        else
                        {
                            var activeBatch = await _db.InventoryBatches
                                .FirstOrDefaultAsync(b => b.ItemCode == entry.ItemCode && b.Status == "ACTIVE");
                            if (activeBatch != null)
                            {
                                resolvedBatchId = activeBatch.BatchId;
                                entry.BatchId = resolvedBatchId;
                            }
                            else
                            {
                                throw new Exception($"No active batch found for ItemCode '{entry.ItemCode}'. A batch must be specified/resolved.");
                            }
                        }

                        var newStock = new InventoryStock
                        {
                            StockId = Guid.NewGuid(),
                            LocationId = entry.LocationId,
                            CustomerId = itemRef.CustomerId,
                            ItemCode = entry.ItemCode,
                            ItemName = itemRef.ItemName,
                            Unit = itemRef.Unit,
                            BatchId = resolvedBatchId,
                            QuantityOnHand = entry.CountedQuantity ?? 0,
                            QuantityAllocated = 0,
                            InboundDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            Status = "AVAILABLE",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            CreatedBy = managerId,
                            PalletCount = entry.CountedPallets ?? 0
                        };

                        _db.InventoryStocks.Add(newStock);
                        await _db.SaveChangesAsync();

                        // Log the Movement
                        var movement = new InventoryMovement
                        {
                            MovementId = Guid.NewGuid(),
                            StockId = newStock.StockId,
                            ItemCode = newStock.ItemCode,
                            BatchId = newStock.BatchId,
                            MovementType = "ADJUSTMENT",
                            Quantity = newStock.QuantityOnHand,
                            FromLocationId = null,
                            ToLocationId = newStock.LocationId,
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            CreatedBy = managerId
                        };
                        _db.InventoryMovements.Add(movement);
                        await _db.SaveChangesAsync();

                        // Log the Adjustment
                        var adjustment = new InventoryAdjustment
                        {
                            AdjustmentId = Guid.NewGuid(),
                            StockId = newStock.StockId,
                            AdjustmentType = InventoryAdjustmentType.CYCLE_COUNT,
                            QuantityBefore = 0,
                            QuantityChanged = newStock.QuantityOnHand,
                            QuantityAfter = newStock.QuantityOnHand,
                            ReasonNotes = $"Cycle Count approved (unexpected stock found). Plan: {plan.PlanCode}. Notes: {dto.ManagerNotes}",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            CreatedBy = managerId,
                            MovementId = movement.MovementId,
                            Status = InventoryAdjustmentStatus.APPROVED,
                            ApprovedBy = managerId,
                            ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            PalletsBefore = 0,
                            PalletsChanged = newStock.PalletCount,
                            PalletsAfter = newStock.PalletCount
                        };
                        _db.InventoryAdjustments.Add(adjustment);
                        await _db.SaveChangesAsync();

                        entry.StockId = newStock.StockId;
                        entry.AdjustmentId = adjustment.AdjustmentId;
                    }
                }
                else
                {
                    entry.Status = CycleCountEntryStatus.REJECTED;
                }

                await _cycleCountRepo.UpdateEntryAsync(entry);
                await _cycleCountRepo.SaveChangesAsync();

                // Transition plan status if all entries are finalized
                var planEntries = await _db.CycleCountEntries.Where(e => e.PlanId == plan.PlanId).ToListAsync();
                bool allReviewed = planEntries.All(e => e.Status == CycleCountEntryStatus.APPROVED || e.Status == CycleCountEntryStatus.REJECTED);
                if (allReviewed)
                {
                    plan.Status = CycleCountPlanStatus.COMPLETED;
                    plan.CompletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    plan.CompletedBy = managerId;
                    await _cycleCountRepo.UpdatePlanAsync(plan);
                    await _cycleCountRepo.SaveChangesAsync();
                }

                if (isOuterTransaction && transaction != null)
                {
                    await transaction.CommitAsync();
                }

                _logger.LogInformation("Variance reviewed for entry {EntryId} by Manager {ManagerId}. Approved: {Approved}", entryId, managerId, dto.Approve);
                return ApiResponse<bool>.SuccessResponse(true, "Variance reviewed successfully.");
            }
            catch (Exception ex)
            {
                if (isOuterTransaction && transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                _logger.LogError(ex, "Failed to review variance for entry {EntryId}", entryId);
                return ApiResponse<bool>.Failure($"Failed to review variance: {ex.Message}");
            }
        }

        public async Task<ApiResponse<CycleCountPlanResponse>> GetPlanDetailsAsync(Guid planId, Guid userId)
        {
            var plan = await _cycleCountRepo.GetPlanByIdAsync(planId);
            if (plan == null)
                return ApiResponse<CycleCountPlanResponse>.Failure("Cycle count plan not found.");

            var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            bool isManagerOrAdmin = user?.Role?.RoleName == "Manager" || user?.Role?.RoleName == "Admin";

            var response = new CycleCountPlanResponse
            {
                PlanId = plan.PlanId,
                PlanCode = plan.PlanCode,
                Status = plan.Status,
                AssignedToUserId = plan.AssignedToUserId,
                AssignedToUsername = plan.AssignedToUser?.Username,
                WarehouseId = plan.WarehouseId,
                WarehouseName = plan.Warehouse.WarehouseName,
                Notes = plan.Notes,
                CreatedAt = plan.CreatedAt,
                CreatedBy = plan.CreatedBy,
                CompletedAt = plan.CompletedAt,
                CompletedBy = plan.CompletedBy,
                Entries = plan.Entries.Select(e => new CycleCountEntryResponse
                {
                    EntryId = e.EntryId,
                    PlanId = e.PlanId,
                    LocationId = e.LocationId,
                    LocationCode = e.Location.LocationCode,
                    StockId = e.StockId,
                    ItemCode = e.ItemCode,
                    BatchId = e.BatchId,
                    BatchNumber = e.Batch?.BatchNumber,
                    
                    // Enforce Blind Count: Pickers see null for System counts and Variance details
                    SystemQuantity = isManagerOrAdmin ? e.SystemQuantity : null,
                    SystemPallets = isManagerOrAdmin ? e.SystemPallets : null,
                    
                    CountedQuantity = e.CountedQuantity,
                    CountedPallets = e.CountedPallets,
                    
                    VarianceQuantity = isManagerOrAdmin ? e.VarianceQuantity : null,
                    VariancePallets = isManagerOrAdmin ? e.VariancePallets : null,
                    
                    Status = e.Status,
                    CountedAt = e.CountedAt,
                    CountedBy = e.CountedBy,
                    ReviewedAt = e.ReviewedAt,
                    ReviewedBy = e.ReviewedBy,
                    ManagerNotes = e.ManagerNotes,
                    AdjustmentId = e.AdjustmentId
                }).ToList()
            };

            return ApiResponse<CycleCountPlanResponse>.SuccessResponse(response, "Plan details retrieved successfully.");
        }

        public async Task<ApiResponse<PagedResult<CycleCountPlanResponse>>> GetPagedPlansAsync(int pageNumber, int pageSize, CycleCountPlanStatus? status, Guid? warehouseId)
        {
            var plans = await _cycleCountRepo.GetPagedPlansAsync(pageNumber, pageSize, status, warehouseId);
            var total = await _cycleCountRepo.CountPlansAsync(status, warehouseId);

            var dtos = plans.Select(p => new CycleCountPlanResponse
            {
                PlanId = p.PlanId,
                PlanCode = p.PlanCode,
                Status = p.Status,
                AssignedToUserId = p.AssignedToUserId,
                AssignedToUsername = p.AssignedToUser?.Username,
                WarehouseId = p.WarehouseId,
                WarehouseName = p.Warehouse.WarehouseName,
                Notes = p.Notes,
                CreatedAt = p.CreatedAt,
                CreatedBy = p.CreatedBy,
                CompletedAt = p.CompletedAt,
                CompletedBy = p.CompletedBy
            }).ToList();

            var pagedResult = PagedResult<CycleCountPlanResponse>.Create(dtos, total, pageNumber, pageSize);
            return ApiResponse<PagedResult<CycleCountPlanResponse>>.SuccessResponse(pagedResult, "Paged plans retrieved successfully.");
        }
    }
}
