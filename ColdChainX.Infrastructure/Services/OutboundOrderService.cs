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
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;

namespace ColdChainX.Infrastructure.Services
{
    public class OutboundOrderService : IOutboundOrderService
    {
        private readonly ApplicationDbContext _db;
        private readonly IInventoryService _inventoryService;
        private readonly ILogger<OutboundOrderService> _logger;

        public OutboundOrderService(
            ApplicationDbContext db,
            IInventoryService inventoryService,
            ILogger<OutboundOrderService> logger)
        {
            _db = db;
            _inventoryService = inventoryService;
            _logger = logger;
        }

        public async Task<ApiResponse<OutboundOrderResponse>> CreateAsync(CreateOutboundOrderRequest request, Guid currentUserId)
        {
            if (request == null)
                return ApiResponse<OutboundOrderResponse>.Failure("Request is null");

            var customer = await _db.Customers.FindAsync(request.CustomerId);
            if (customer == null)
                return ApiResponse<OutboundOrderResponse>.Failure("Customer not found.");

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
        }

        public async Task<ApiResponse<PagedResult<OutboundOrderResponse>>> GetListAsync(int pageNumber, int pageSize, string? search = null)
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
        }

        public async Task<ApiResponse<OutboundOrderResponse>> AllocateOrderAsync(Guid outboundOrderId, Guid userId)
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
        }

        public async Task<ApiResponse<bool>> CancelOrderAsync(Guid outboundOrderId, Guid userId)
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
        }

        public async Task<ApiResponse<OutboundOrderResponse>> StartPickingAsync(Guid outboundOrderId, Guid pickerId, Guid userId)
        {
            var pickerExists = await _db.Users.AnyAsync(u => u.UserId == pickerId);
            if (!pickerExists)
                return ApiResponse<OutboundOrderResponse>.Failure("Picker user not found.");

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
        }

        public async Task<ApiResponse<OutboundOrderResponse>> CompletePickingAsync(Guid outboundOrderId, Guid userId)
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
        }

        public async Task<ApiResponse<OutboundOrderResponse>> ShipOrderAsync(Guid outboundOrderId, Guid userId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.OutboundOrders.FindAsync(outboundOrderId);
                if (order == null)
                    return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                if (order.Status != OutboundOrderStatus.PICKED)
                    return ApiResponse<OutboundOrderResponse>.Failure("Shipping is only allowed from PICKED status.");

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
                await transaction.CommitAsync();

                _logger.LogInformation("Outbound order {OrderCode} shipped physically and stock deducted.", order.OrderCode);
                return await GetByIdAsync(outboundOrderId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction failed for shipping order: {Id}", outboundOrderId);
                return ApiResponse<OutboundOrderResponse>.Failure($"Failed to ship order: {ex.Message}");
            }
        }

        public async Task<ApiResponse<OutboundOrderResponse>> CompleteOrderAsync(Guid outboundOrderId, Guid userId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.OutboundOrders.FindAsync(outboundOrderId);
                if (order == null)
                    return ApiResponse<OutboundOrderResponse>.Failure("Outbound order not found.");

                if (order.Status != OutboundOrderStatus.SHIPPED)
                    return ApiResponse<OutboundOrderResponse>.Failure("Order can only be completed from SHIPPED status.");

                order.Status = OutboundOrderStatus.COMPLETED;
                order.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                order.UpdatedBy = userId;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return await GetByIdAsync(outboundOrderId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return ApiResponse<OutboundOrderResponse>.Failure($"Failed to complete order: {ex.Message}");
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
