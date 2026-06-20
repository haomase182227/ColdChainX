using ColdChainX.Application.DTOs.Asns;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ColdChainX.Infrastructure.Services
{
    public class AsnService : IAsnService
    {
        private const string ContractSigned = "CONTRACT_SIGNED";
        private readonly ApplicationDbContext _db;

        public AsnService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<PagedResult<InboundScheduleResponse>>> GetInboundSchedulesAsync(
            Guid? customerId,
            string? status,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? searchQuery,
            Guid? warehouseId,
            Guid? orderId,
            int pageNumber,
            int pageSize)
        {
            var query = _db.InboundAsns
                .Include(a => a.Order)
                    .ThenInclude(o => o.Customer)
                .Include(a => a.Order)
                    .ThenInclude(o => o.DestLocationNavigation)
                .Include(a => a.Order)
                    .ThenInclude(o => o.WarehouseReceipts)
                .AsNoTracking();

            // Filter by Order ID
            if (orderId.HasValue)
            {
                query = query.Where(a => a.OrderId == orderId.Value);
            }

            // Filter by Customer
            if (customerId.HasValue)
            {
                query = query.Where(a => a.Order.CustomerId == customerId.Value);
            }

            // Filter by Status
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(a => a.Status == status.Trim());
            }

            // Filter by Date Range
            if (dateFrom.HasValue)
            {
                query = query.Where(a => a.RequestedDropoffTime >= dateFrom.Value);
            }
            if (dateTo.HasValue)
            {
                query = query.Where(a => a.RequestedDropoffTime <= dateTo.Value);
            }

            // Filter by Search Query
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var search = searchQuery.Trim().ToLower();
                query = query.Where(a => a.AsnCode.ToLower().Contains(search)
                    || a.Order.TrackingCode.ToLower().Contains(search)
                    || a.Order.ItemName.ToLower().Contains(search)
                    || (a.Order.Customer != null && a.Order.Customer.CompanyName.ToLower().Contains(search))
                    || (a.Order.DestLocationNavigation != null && a.Order.DestLocationNavigation.Address.ToLower().Contains(search)));
            }

            // Filter by Warehouse (pre-receipt address match or post-receipt link)
            if (warehouseId.HasValue)
            {
                var warehouse = await _db.Warehouses.FindAsync(warehouseId.Value);
                if (warehouse != null)
                {
                    var whName = warehouse.WarehouseName.ToLower();
                    var whCode = warehouse.WarehouseCode.ToLower();
                    var whAddress = warehouse.Address?.ToLower() ?? string.Empty;

                    query = query.Where(a =>
                        a.Order.WarehouseReceipts.Any(wr => wr.WarehouseId == warehouseId.Value)
                        || (a.Order.DestLocationNavigation != null && (
                            a.Order.DestLocationNavigation.Address.ToLower().Contains(whName)
                            || a.Order.DestLocationNavigation.Address.ToLower().Contains(whCode)
                            || (!string.IsNullOrEmpty(whAddress) && a.Order.DestLocationNavigation.Address.ToLower().Contains(whAddress))
                        ))
                    );
                }
            }

            // Sort by earliest requested drop-off time (upcoming first)
            query = query.OrderBy(a => a.RequestedDropoffTime);

            var totalRecords = await query.CountAsync();
            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var warehouses = await _db.Warehouses.ToListAsync();
            var mappedList = new List<InboundScheduleResponse>();

            foreach (var item in items)
            {
                Guid? matchedWarehouseId = null;
                string? matchedWarehouseName = null;

                var receipt = item.Order.WarehouseReceipts.FirstOrDefault();
                if (receipt != null)
                {
                    matchedWarehouseId = receipt.WarehouseId;
                    matchedWarehouseName = receipt.Warehouse?.WarehouseName 
                        ?? warehouses.FirstOrDefault(w => w.WarehouseId == receipt.WarehouseId)?.WarehouseName;
                }
                else if (item.Order.DestLocationNavigation != null)
                {
                    var destAddress = item.Order.DestLocationNavigation.Address.ToLower();
                    var matchedWh = warehouses.FirstOrDefault(w =>
                        destAddress.Contains(w.WarehouseName.ToLower())
                        || destAddress.Contains(w.WarehouseCode.ToLower())
                        || (!string.IsNullOrEmpty(w.Address) && destAddress.Contains(w.Address.ToLower()))
                    );

                    if (matchedWh != null)
                    {
                        matchedWarehouseId = matchedWh.WarehouseId;
                        matchedWarehouseName = matchedWh.WarehouseName;
                    }
                }

                mappedList.Add(new InboundScheduleResponse
                {
                    AsnId = item.AsnId,
                    AsnCode = item.AsnCode,
                    OrderId = item.OrderId,
                    TrackingCode = item.Order.TrackingCode,
                    CustomerId = item.Order.CustomerId,
                    CustomerName = item.Order.Customer?.CompanyName ?? "Khách hàng vãng lai",
                    ItemName = item.Order.ItemName,
                    Category = item.Order.Category,
                    Quantity = item.Order.Quantity,
                    TempCondition = item.Order.TempCondition,
                    ExpectedWeightKg = item.Order.ExpectedWeightKg,
                    ExpectedCbm = item.Order.ExpectedCbm,
                    DestAddress = item.Order.DestLocationNavigation?.Address ?? "Không xác định",
                    RequestedDropoffTime = item.RequestedDropoffTime,
                    Status = item.Status,
                    QrCodeValue = item.QrCodeValue,
                    CreatedAt = item.CreatedAt,
                    WarehouseId = matchedWarehouseId,
                    WarehouseName = matchedWarehouseName
                });
            }

            var pagedResult = PagedResult<InboundScheduleResponse>.Create(mappedList, totalRecords, pageNumber, pageSize);
            return ApiResponse<PagedResult<InboundScheduleResponse>>.SuccessResponse(pagedResult, "Inbound schedules retrieved successfully.");
        }

        public async Task<ApiResponse<AsnResponse>> CreateAsnAsync(CreateAsnRequest request, Guid customerId)
        {
            if (request.OrderId == Guid.Empty)
                return ApiResponse<AsnResponse>.Failure("OrderId is required");

            var order = await _db.TransportOrders
                .Include(o => o.Route)
                .FirstOrDefaultAsync(o => o.OrderId == request.OrderId);

            if (order == null)
                return ApiResponse<AsnResponse>.Failure("Order not found");

            if (order.CustomerId != customerId)
                return ApiResponse<AsnResponse>.Failure("Order does not belong to this customer");

            if (!string.Equals(order.Status, ContractSigned, StringComparison.OrdinalIgnoreCase))
                return ApiResponse<AsnResponse>.Failure("ASN can only be created after contract is signed");

            if (order.Route == null)
                return ApiResponse<AsnResponse>.Failure("Order has no selected route");

            var requestedDropoff = DateTime.SpecifyKind(request.RequestedDropoffTime, DateTimeKind.Unspecified);
            var latestDropoffTime = order.Route.CutOffTime.Subtract(TimeSpan.FromHours(2));
            
            if (requestedDropoff.TimeOfDay > latestDropoffTime)
            {
                return ApiResponse<AsnResponse>.Failure(
                    $"Requested_Dropoff_Time must be at least 2 hours before the route cut-off time ({order.Route.CutOffTime:hh\\:mm\\:ss}). Latest allowed drop-off time is {latestDropoffTime:hh\\:mm\\:ss}");
            }

            var asnCode = await GenerateUniqueAsnCodeAsync();
            var qrValue = $"ASN|{asnCode}|ORDER|{order.OrderId}|ROUTE|{order.Route.RouteCode}|DROPOFF|{requestedDropoff:O}";

            var asn = new Core.Entities.InboundAsn
            {
                AsnId = Guid.NewGuid(),
                AsnCode = asnCode,
                OrderId = order.OrderId,
                RequestedDropoffTime = requestedDropoff,
                QrCodeValue = qrValue,
                Status = "SCHEDULED",
                CreatedAt = DbNow()
            };

            _db.InboundAsns.Add(asn);
            await _db.SaveChangesAsync();

            return ApiResponse<AsnResponse>.SuccessResponse(new AsnResponse
            {
                AsnId = asn.AsnId,
                AsnCode = asn.AsnCode,
                OrderId = asn.OrderId,
                RouteId = order.Route.RouteId,
                RouteCode = order.Route.RouteCode,
                RequestedDropoffTime = asn.RequestedDropoffTime,
                CutOffTime = order.Route.CutOffTime,
                QrCodeValue = asn.QrCodeValue,
                Status = asn.Status,
                CreatedAt = asn.CreatedAt
            }, "ASN created successfully");
        }

        private async Task<string> GenerateUniqueAsnCodeAsync()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var value = $"ASN-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                if (!await _db.InboundAsns.AnyAsync(a => a.AsnCode == value))
                    return value;
            }

            return $"ASN-{Guid.NewGuid():N}"[..24];
        }

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }
}
