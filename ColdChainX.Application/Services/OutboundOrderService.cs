using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Outbound;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;

namespace ColdChainX.Application.Services
{
    public class OutboundOrderService : IOutboundOrderService
    {
        private readonly IApplicationDbContext _db;
        private readonly IInventoryService _inventoryService;
        private readonly IWarehouseAttachmentRepository _attachmentRepository;
        private readonly ComplianceRulesEngine _complianceEngine;
        private readonly ILogger<OutboundOrderService> _logger;

        public OutboundOrderService(
            IApplicationDbContext db,
            IInventoryService inventoryService,
            IWarehouseAttachmentRepository attachmentRepository,
            ComplianceRulesEngine complianceEngine,
            ILogger<OutboundOrderService> logger)
        {
            _db = db;
            _inventoryService = inventoryService;
            _attachmentRepository = attachmentRepository;
            _complianceEngine = complianceEngine;
            _logger = logger;
        }

        public async Task<ApiResponse<OutboundOrderResponse>> CreateAsync(CreateOutboundOrderRequest request, Guid currentUserId)
        {
            if (request == null)
                return ApiResponse<OutboundOrderResponse>.Failure("Request is null");

            var customer = await _db.Customers.FindAsync(request.CustomerId);
            if (customer == null)
                return ApiResponse<OutboundOrderResponse>.Failure("Customer not found.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var orderCode = $"OB-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
                    
                    var order = new OutboundOrder
                    {
                        OutboundOrderId = Guid.NewGuid(),
                        OrderCode = orderCode,
                        CustomerId = request.CustomerId,
                        ReceiverName = request.ReceiverName.Trim(),
                        ReceiverPhone = request.ReceiverPhone.Trim(),
                        DestinationAddress = request.DestinationAddress.Trim(),
                        Status = OutboundOrderStatus.DRAFT,
                        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                        CreatedBy = currentUserId
                    };

                    _db.OutboundOrders.Add(order);

                    foreach (var itemRequest in request.Items)
                    {
                        var item = new OutboundOrderItem
                        {
                            OutboundOrderItemId = Guid.NewGuid(),
                            OutboundOrderId = order.OutboundOrderId,
                            ItemCode = itemRequest.ItemCode.Trim(),
                            ItemName = itemRequest.ItemName.Trim(),
                            Unit = itemRequest.Unit.Trim(),
                            Quantity = itemRequest.Quantity
                        };
                        _db.OutboundOrderItems.Add(item);
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Outbound order {OrderCode} created in DRAFT status.", order.OrderCode);
                    return await GetByIdAsync(order.OutboundOrderId);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Failed to create outbound order.");
                    return ApiResponse<OutboundOrderResponse>.Failure($"Failed to create outbound order: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<PagedResult<OutboundOrderResponse>>> GetListAsync(
            int pageNumber, 
            int pageSize, 
            string? search = null, 
            string? status = null, 
            Guid? customerId = null)
        {
            try
            {
                var query = _db.OutboundOrders
                    .Include(o => o.Customer)
                    .Include(o => o.OutboundOrderItems)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var cleanSearch = search.Trim();
                    query = query.Where(o => o.OrderCode.Contains(cleanSearch) 
                                             || o.ReceiverName.Contains(cleanSearch)
                                             || o.Customer.CompanyName.Contains(cleanSearch));
                }

                if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OutboundOrderStatus>(status, true, out var statusEnum))
                {
                    query = query.Where(o => o.Status == statusEnum);
                }

                if (customerId.HasValue && customerId.Value != Guid.Empty)
                {
                    query = query.Where(o => o.CustomerId == customerId.Value);
                }

                query = query.OrderByDescending(o => o.CreatedAt);

                int totalCount = await query.CountAsync();
                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var dtos = items.Select(MapToResponse).ToList();
                var pagedResult = PagedResult<OutboundOrderResponse>.Create(dtos, totalCount, pageNumber, pageSize);

                return ApiResponse<PagedResult<OutboundOrderResponse>>.SuccessResponse(pagedResult, "Outbound orders retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve outbound orders list.");
                return ApiResponse<PagedResult<OutboundOrderResponse>>.Failure($"Failed to retrieve outbound orders: {ex.Message}");
            }
        }

        public async Task<ApiResponse<OutboundOrderResponse>> GetByIdAsync(Guid outboundOrderId)
        {
            try
            {
                var order = await _db.OutboundOrders
                    .Include(o => o.Customer)
                    .Include(o => o.AssignedPicker)
                    .Include(o => o.OutboundOrderItems)
                    .FirstOrDefaultAsync(o => o.OutboundOrderId == outboundOrderId);

                if (order == null)
                    return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                return ApiResponse<OutboundOrderResponse>.SuccessResponse(MapToResponse(order), "Outbound order retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve outbound order details for ID: {Id}", outboundOrderId);
                return ApiResponse<OutboundOrderResponse>.Failure($"Failed to retrieve outbound order details: {ex.Message}");
            }
        }

        public async Task<ApiResponse<OutboundOrderResponse>> UpdateAsync(Guid outboundOrderId, UpdateOutboundOrderRequest request, Guid currentUserId)
        {
            if (request == null)
                return ApiResponse<OutboundOrderResponse>.Failure("Request is null");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var order = await _db.OutboundOrders
                        .Include(o => o.OutboundOrderItems)
                        .FirstOrDefaultAsync(o => o.OutboundOrderId == outboundOrderId);

                    if (order == null)
                        return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                    if (order.Status != OutboundOrderStatus.DRAFT)
                        return ApiResponse<OutboundOrderResponse>.Failure("Only outbound orders in DRAFT status can be modified.");

                    order.ReceiverName = request.ReceiverName.Trim();
                    order.ReceiverPhone = request.ReceiverPhone.Trim();
                    order.DestinationAddress = request.DestinationAddress.Trim();
                    order.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    order.UpdatedBy = currentUserId;

                    // Remove old items
                    _db.OutboundOrderItems.RemoveRange(order.OutboundOrderItems);

                    // Add new items
                    foreach (var itemRequest in request.Items)
                    {
                        var item = new OutboundOrderItem
                        {
                            OutboundOrderItemId = Guid.NewGuid(),
                            OutboundOrderId = order.OutboundOrderId,
                            ItemCode = itemRequest.ItemCode.Trim(),
                            ItemName = itemRequest.ItemName.Trim(),
                            Unit = itemRequest.Unit.Trim(),
                            Quantity = itemRequest.Quantity
                        };
                        _db.OutboundOrderItems.Add(item);
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Outbound order {OrderCode} updated.", order.OrderCode);
                    return await GetByIdAsync(order.OutboundOrderId);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Failed to update outbound order: {Id}", outboundOrderId);
                    return ApiResponse<OutboundOrderResponse>.Failure($"Failed to update outbound order: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<OutboundOrderResponse>> AllocateOrderAsync(Guid outboundOrderId, Guid userId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var order = await _db.OutboundOrders
                        .Include(o => o.OutboundOrderItems)
                        .FirstOrDefaultAsync(o => o.OutboundOrderId == outboundOrderId);

                    if (order == null)
                        return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                    if (order.Status != OutboundOrderStatus.DRAFT)
                        return ApiResponse<OutboundOrderResponse>.Failure("Only DRAFT orders can be allocated.");

                    // Construct allocation request
                    var allocationRequest = new AllocateInventoryRequest
                    {
                        ReferenceDocumentId = order.OutboundOrderId,
                        Items = order.OutboundOrderItems.Select(i => new AllocateInventoryItemRequest
                        {
                            ItemCode = i.ItemCode,
                            Quantity = i.Quantity
                        }).ToList()
                    };

                    // Perform allocation
                    var allocationResult = await _inventoryService.AllocateStockAsync(allocationRequest, userId);
                    if (!allocationResult.Success)
                    {
                        await transaction.RollbackAsync();
                        return ApiResponse<OutboundOrderResponse>.Failure($"Allocation failed: {allocationResult.Message}");
                    }

                    order.Status = OutboundOrderStatus.ALLOCATED;
                    order.AllocatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    order.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    order.UpdatedBy = userId;

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Outbound order {OrderCode} allocated successfully.", order.OrderCode);
                    return await GetByIdAsync(order.OutboundOrderId);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Transaction failed for allocating order: {Id}", outboundOrderId);
                    return ApiResponse<OutboundOrderResponse>.Failure($"Allocation transaction failed: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<bool>> CancelOrderAsync(Guid outboundOrderId, Guid userId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var order = await _db.OutboundOrders.FindAsync(outboundOrderId);
                    if (order == null)
                        return ApiResponse<bool>.Failure("Outbound order not found.");

                    var cancelable = new[] { OutboundOrderStatus.DRAFT, OutboundOrderStatus.ALLOCATED, OutboundOrderStatus.PICKING, OutboundOrderStatus.PICKED };
                    if (!cancelable.Contains(order.Status))
                    {
                        return ApiResponse<bool>.Failure($"Cannot cancel order in status '{order.Status}'.");
                    }

                    // Auto release allocations if active
                    if (order.Status == OutboundOrderStatus.ALLOCATED 
                        || order.Status == OutboundOrderStatus.PICKING 
                        || order.Status == OutboundOrderStatus.PICKED)
                    {
                        var releaseResult = await _inventoryService.ReleaseAllocationAsync(new ReleaseAllocationRequest
                        {
                            ReferenceDocumentId = order.OutboundOrderId
                        }, userId);

                        if (!releaseResult.Success)
                        {
                            await transaction.RollbackAsync();
                            return ApiResponse<bool>.Failure($"Failed to release order allocations: {releaseResult.Message}");
                        }
                    }

                    order.Status = OutboundOrderStatus.CANCELLED;
                    order.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    order.UpdatedBy = userId;

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Outbound order {OrderCode} cancelled successfully.", order.OrderCode);
                    return ApiResponse<bool>.SuccessResponse(true, "Outbound order cancelled successfully and associated allocations released.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Transaction failed for cancelling order: {Id}", outboundOrderId);
                    return ApiResponse<bool>.Failure($"Cancellation transaction failed: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<OutboundOrderResponse>> StartPickingAsync(Guid outboundOrderId, Guid pickerId, Guid userId)
        {
            var pickerExists = await _db.Users.AnyAsync(u => u.UserId == pickerId);
            if (!pickerExists)
                return ApiResponse<OutboundOrderResponse>.Failure("Picker user not found.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var order = await _db.OutboundOrders.FindAsync(outboundOrderId);
                    if (order == null)
                        return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                    if (order.Status != OutboundOrderStatus.ALLOCATED)
                        return ApiResponse<OutboundOrderResponse>.Failure("Picking can only start from ALLOCATED status.");

                    order.Status = OutboundOrderStatus.PICKING;
                    order.AssignedPickerId = pickerId;
                    order.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    order.UpdatedBy = userId;

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return await GetByIdAsync(outboundOrderId);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return ApiResponse<OutboundOrderResponse>.Failure($"Failed to start picking: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<OutboundOrderResponse>> CompletePickingAsync(Guid outboundOrderId, Guid userId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var order = await _db.OutboundOrders.FindAsync(outboundOrderId);
                    if (order == null)
                        return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                    if (order.Status != OutboundOrderStatus.PICKING)
                        return ApiResponse<OutboundOrderResponse>.Failure("Picking can only complete from PICKING status.");

                    order.Status = OutboundOrderStatus.PICKED;
                    order.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    order.UpdatedBy = userId;

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return await GetByIdAsync(outboundOrderId);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return ApiResponse<OutboundOrderResponse>.Failure($"Failed to complete picking: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<OutboundOrderResponse>> ShipOrderAsync(Guid outboundOrderId, Guid userId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var isOuterTransaction = _db.Database.CurrentTransaction == null;
                using var transaction = isOuterTransaction ? await _db.Database.BeginTransactionAsync() : null;
                try
                {
                    var order = await _db.OutboundOrders
                        .Include(o => o.OutboundOrderItems)
                        .FirstOrDefaultAsync(o => o.OutboundOrderId == outboundOrderId);

                    if (order == null)
                        return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                    if (order.Status != OutboundOrderStatus.PICKED)
                        return ApiResponse<OutboundOrderResponse>.Failure("Shipping is only allowed from PICKED status.");

                    // Compliance Validation
                    var attachments = await _attachmentRepository.GetAttachmentsByOutboundOrderIdAsync(outboundOrderId);
                    var itemCodes = order.OutboundOrderItems.Select(i => i.ItemCode).ToList();
                    var categories = await _db.WarehouseReceiptItems
                        .Where(ri => itemCodes.Contains(ri.ItemCode))
                        .Select(ri => new { ri.ItemCode, ri.ProductCategory })
                        .Distinct()
                        .ToListAsync();

                    var itemCategories = categories
                        .GroupBy(c => c.ItemCode)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.ProductCategory).ToList()
                        );

                    var complianceResult = _complianceEngine.ValidateOutboundOrder(order, attachments, itemCategories);

                    if (!complianceResult.Passed)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("Outbound shipment blocked due to compliance validation failure.");

                        if (complianceResult.MissingRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Missing:");
                            foreach (var req in complianceResult.MissingRequirements)
                            {
                                sb.AppendLine($"* {req}");
                            }
                        }

                        if (complianceResult.PendingRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Pending:");
                            foreach (var req in complianceResult.PendingRequirements)
                            {
                                sb.AppendLine($"* {req}");
                            }
                        }

                        if (complianceResult.FailedRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Failed:");
                            foreach (var req in complianceResult.FailedRequirements)
                            {
                                sb.AppendLine($"* {req}");
                            }
                        }

                        return ApiResponse<OutboundOrderResponse>.Failure(sb.ToString().TrimEnd());
                    }

                    // Lock the allocated stock records pessimistically to prevent lost updates
                    var stockIds = await _db.InventoryAllocations
                        .Where(a => a.ReferenceDocumentId == order.OutboundOrderId && a.Status == "ALLOCATED")
                        .Select(a => a.StockId)
                        .Distinct()
                        .ToListAsync();

                    if (_db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
                    {
                        foreach (var stockId in stockIds)
                        {
                            await _db.InventoryStocks
                                .FromSqlRaw("SELECT * FROM inventory_stocks WHERE stock_id = {0} FOR UPDATE", stockId)
                                .FirstOrDefaultAsync();
                        }
                    }

                    // 1. Fetch all active allocations for the document ID
                    var allocations = await _db.InventoryAllocations
                        .Include(a => a.Stock)
                        .ThenInclude(s => s.Location)
                        .ThenInclude(l => l.Zone)
                        .Where(a => a.ReferenceDocumentId == order.OutboundOrderId && a.Status == "ALLOCATED")
                        .ToListAsync();

                    if (!allocations.Any())
                    {
                        return ApiResponse<OutboundOrderResponse>.Failure("No allocations found. Allocations might have been cancelled.");
                    }

                    // 2. Perform physical deduction of inventory (de-allocation & de-stocking)
                    foreach (var allocation in allocations)
                    {
                        var stock = allocation.Stock;
                        
                        // Deduct quantity on hand and quantity allocated
                        stock.QuantityOnHand -= allocation.AllocatedQuantity;
                        stock.QuantityAllocated -= allocation.AllocatedQuantity;

                        // Ensure bounds checking
                        if (stock.QuantityOnHand < 0) stock.QuantityOnHand = 0;
                        if (stock.QuantityAllocated < 0) stock.QuantityAllocated = 0;

                        stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                        stock.UpdatedBy = userId;

                        // Update location and zone pallet counts on depletion
                        if (stock.QuantityOnHand == 0)
                        {
                            int prePallets = stock.PalletCount;
                            stock.Status = "INACTIVE";
                            stock.PalletCount = 0;

                            if (stock.Location != null)
                            {
                                stock.Location.CurrentPallets -= prePallets;
                                if (stock.Location.CurrentPallets < 0) stock.Location.CurrentPallets = 0;

                                if (stock.Location.Zone != null)
                                {
                                    stock.Location.Zone.CurrentPallets -= prePallets;
                                    if (stock.Location.Zone.CurrentPallets < 0) stock.Location.Zone.CurrentPallets = 0;
                                }
                            }
                        }

                        // Mark allocation as completed
                        allocation.Status = "COMPLETED";

                        // Log ledger entry
                        var movement = new InventoryMovement
                        {
                            MovementId = Guid.NewGuid(),
                            StockId = stock.StockId,
                            ItemCode = stock.ItemCode,
                            BatchId = stock.BatchId,
                            MovementType = "OUTBOUND",
                            Quantity = allocation.AllocatedQuantity,
                            FromLocationId = stock.LocationId,
                            ToLocationId = null,
                            ReferenceDocumentId = order.OutboundOrderId,
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            CreatedBy = userId
                        };
                        _db.InventoryMovements.Add(movement);
                    }

                    order.Status = OutboundOrderStatus.SHIPPED;
                    order.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    order.UpdatedBy = userId;

                    await _db.SaveChangesAsync();
                    if (isOuterTransaction && transaction != null)
                    {
                        await transaction.CommitAsync();
                    }

                    _logger.LogInformation("Outbound order {OrderCode} shipped physically and stock deducted.", order.OrderCode);
                    return await GetByIdAsync(outboundOrderId);
                }
                catch (Exception ex)
                {
                    if (isOuterTransaction && transaction != null)
                    {
                        await transaction.RollbackAsync();
                    }
                    _logger.LogError(ex, "Transaction failed for shipping order: {Id}", outboundOrderId);
                    return ApiResponse<OutboundOrderResponse>.Failure($"Failed to ship order: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<AllocationResponse>> GetAllocationsAsync(Guid outboundOrderId)
        {
            try
            {
                var order = await _db.OutboundOrders.FindAsync(outboundOrderId);
                if (order == null)
                    return ApiResponse<AllocationResponse>.Failure("Outbound order not found.");

                var allocations = await _db.InventoryAllocations
                    .Include(a => a.Stock)
                        .ThenInclude(s => s.Batch)
                    .Include(a => a.Stock)
                        .ThenInclude(s => s.Location)
                            .ThenInclude(l => l.Zone)
                    .Where(a => a.ReferenceDocumentId == outboundOrderId)
                    .ToListAsync();

                var response = new AllocationResponse
                {
                    OutboundOrderId = order.OutboundOrderId,
                    OrderCode = order.OrderCode,
                    Allocations = allocations.Select(a => new AllocationItemDto
                    {
                        AllocationId = a.AllocationId,
                        StockId = a.StockId,
                        ItemCode = a.Stock.ItemCode,
                        ItemName = a.Stock.ItemName,
                        BatchNumber = a.Stock.Batch.BatchNumber,
                        ExpiryDate = a.Stock.Batch.ExpiryDate,
                        LocationCode = a.Stock.Location.LocationCode,
                        ZoneCode = a.Stock.Location.Zone?.ZoneCode ?? "N/A",
                        AllocatedQuantity = a.AllocatedQuantity,
                        AvailableQuantity = a.Stock.QuantityOnHand - a.Stock.QuantityAllocated,
                        Status = a.Status
                    }).ToList()
                };

                return ApiResponse<AllocationResponse>.SuccessResponse(response, "Allocations retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve allocations for order: {Id}", outboundOrderId);
                return ApiResponse<AllocationResponse>.Failure($"Failed to retrieve allocations: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PickingListResponse>> GetPickingListAsync(Guid outboundOrderId)
        {
            try
            {
                var order = await _db.OutboundOrders
                    .Include(o => o.AssignedPicker)
                    .FirstOrDefaultAsync(o => o.OutboundOrderId == outboundOrderId);

                if (order == null)
                    return ApiResponse<PickingListResponse>.Failure("Outbound order not found.");

                var allocations = await _db.InventoryAllocations
                    .Include(a => a.Stock)
                        .ThenInclude(s => s.Batch)
                    .Include(a => a.Stock)
                        .ThenInclude(s => s.Location)
                            .ThenInclude(l => l.Zone)
                    .Where(a => a.ReferenceDocumentId == outboundOrderId && a.Status == "ALLOCATED")
                    .ToListAsync();

                // Sort picking list items by LocationCode ascending
                var pickingItems = allocations
                    .Select(a => new PickingListItemDto
                    {
                        ItemCode = a.Stock.ItemCode,
                        ItemName = a.Stock.ItemName,
                        LocationId = a.Stock.LocationId,
                        LocationCode = a.Stock.Location.LocationCode,
                        ZoneCode = a.Stock.Location.Zone?.ZoneCode ?? "N/A",
                        BatchNumber = a.Stock.Batch.BatchNumber,
                        ExpiryDate = a.Stock.Batch.ExpiryDate,
                        QuantityToPick = a.AllocatedQuantity
                    })
                    .OrderBy(pi => pi.LocationCode)
                    .ToList();

                var response = new PickingListResponse
                {
                    OutboundOrderId = order.OutboundOrderId,
                    OrderCode = order.OrderCode,
                    AssignedPickerId = order.AssignedPickerId,
                    AssignedPickerName = order.AssignedPicker?.FullName,
                    Items = pickingItems
                };

                return ApiResponse<PickingListResponse>.SuccessResponse(response, "Picking list retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve picking list for order: {Id}", outboundOrderId);
                return ApiResponse<PickingListResponse>.Failure($"Failed to retrieve picking list: {ex.Message}");
            }
        }

        private OutboundOrderResponse MapToResponse(OutboundOrder order)
        {
            return new OutboundOrderResponse
            {
                OutboundOrderId = order.OutboundOrderId,
                OrderCode = order.OrderCode,
                CustomerId = order.CustomerId,
                CustomerName = order.Customer?.CompanyName ?? "Unknown Customer",
                ReceiverName = order.ReceiverName,
                ReceiverPhone = order.ReceiverPhone,
                DestinationAddress = order.DestinationAddress,
                Status = order.Status.ToString(),
                AssignedPickerId = order.AssignedPickerId,
                AssignedPickerName = order.AssignedPicker?.FullName,
                AllocatedAt = order.AllocatedAt,
                CreatedAt = order.CreatedAt,
                Items = order.OutboundOrderItems?.Select(i => new OutboundOrderItemResponse
                {
                    OutboundOrderItemId = i.OutboundOrderItemId,
                    ItemCode = i.ItemCode,
                    ItemName = i.ItemName,
                    Unit = i.Unit,
                    Quantity = i.Quantity
                }).ToList() ?? new List<OutboundOrderItemResponse>()
            };
        }
    }
}
