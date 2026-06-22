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
        private readonly ComplianceRulesEngine _complianceEngine;
        private readonly ILogger<OutboundOrderService> _logger;

        public OutboundOrderService(
            IApplicationDbContext db,
            ComplianceRulesEngine complianceEngine,
            ILogger<OutboundOrderService> logger)
        {
            _db = db;
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

        public Task<ApiResponse<OutboundOrderResponse>> AllocateOrderAsync(Guid outboundOrderId, Guid userId)
        {
            return Task.FromResult(ApiResponse<OutboundOrderResponse>.Failure("Inventory module has been removed."));
        }

        public async Task<ApiResponse<bool>> CancelOrderAsync(Guid outboundOrderId, Guid userId)
        {
            var order = await _db.OutboundOrders.FindAsync(outboundOrderId);
            if (order == null) return ApiResponse<bool>.Failure("Order not found.");
            order.Status = OutboundOrderStatus.CANCELLED;
            await _db.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Cancelled.");
        }

        public Task<ApiResponse<OutboundOrderResponse>> StartPickingAsync(Guid outboundOrderId, Guid pickerId, Guid userId)
        {
            return Task.FromResult(ApiResponse<OutboundOrderResponse>.Failure("Inventory module has been removed."));
        }

        public Task<ApiResponse<OutboundOrderResponse>> CompletePickingAsync(Guid outboundOrderId, Guid userId)
        {
            return Task.FromResult(ApiResponse<OutboundOrderResponse>.Failure("Inventory module has been removed."));
        }

        public Task<ApiResponse<OutboundOrderResponse>> ShipOrderAsync(Guid outboundOrderId, Guid userId)
        {
            return Task.FromResult(ApiResponse<OutboundOrderResponse>.Failure("Inventory module has been removed."));
        }

        public Task<ApiResponse<AllocationResponse>> GetAllocationsAsync(Guid outboundOrderId)
        {
            return Task.FromResult(ApiResponse<AllocationResponse>.Failure("Inventory module has been removed."));
        }

        public Task<ApiResponse<PickingListResponse>> GetPickingListAsync(Guid outboundOrderId)
        {
            return Task.FromResult(ApiResponse<PickingListResponse>.Failure("Inventory module has been removed."));
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
