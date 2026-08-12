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
        private readonly IPdfGeneratorService _pdfGeneratorService;
        private readonly IFileService _fileService;

        public AsnService(ApplicationDbContext db, IPdfGeneratorService pdfGeneratorService, IFileService fileService)
        {
            _db = db;
            _pdfGeneratorService = pdfGeneratorService;
            _fileService = fileService;
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

            if (orderId.HasValue)
            {
                query = query.Where(a => a.OrderId == orderId.Value);
            }

            if (customerId.HasValue)
            {
                query = query.Where(a => a.Order.CustomerId == customerId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(a => a.Status == status.Trim());
            }

            if (dateFrom.HasValue)
            {
                query = query.Where(a => a.RequestedDropoffTime >= dateFrom.Value);
            }
            if (dateTo.HasValue)
            {
                if (dateTo.Value.TimeOfDay == TimeSpan.Zero)
                {
                    var exclusiveDateTo = dateTo.Value.Date.AddDays(1);
                    query = query.Where(a => a.RequestedDropoffTime < exclusiveDateTo);
                }
                else
                {
                    query = query.Where(a => a.RequestedDropoffTime <= dateTo.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var search = searchQuery.Trim().ToLower();
                query = query.Where(a => a.AsnCode.ToLower().Contains(search)
                    || a.Order.TrackingCode.ToLower().Contains(search)
                    || a.Order.ItemName.ToLower().Contains(search)
                    || (a.Order.Customer != null && a.Order.Customer.CompanyName.ToLower().Contains(search))
                    || (a.Order.DestLocationNavigation != null && a.Order.DestLocationNavigation.Address.ToLower().Contains(search)));
            }

            if (warehouseId.HasValue)
            {
                query = query.Where(a => a.WarehouseId == warehouseId.Value);
            }

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

                if (item.WarehouseId.HasValue)
                {
                    matchedWarehouseId = item.WarehouseId;
                    matchedWarehouseName = warehouses
                        .FirstOrDefault(w => w.WarehouseId == item.WarehouseId.Value)
                        ?.WarehouseName;
                }
                else
                {
                    var receipt = item.Order.WarehouseReceipts.FirstOrDefault();
                    if (receipt != null)
                    {
                        matchedWarehouseId = receipt.WarehouseId;
                        matchedWarehouseName = warehouses
                            .FirstOrDefault(w => w.WarehouseId == receipt.WarehouseId)
                            ?.WarehouseName;
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
                    ExpectedWeightKg = (item.Order.OrderDimension?.ExpectedWeightKg ?? 0m),
                    ExpectedCbm = (item.Order.OrderDimension?.ExpectedCbm ?? 0m),
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
                .Include(o => o.Schedule)
                    .ThenInclude(s => s!.Route)
                .FirstOrDefaultAsync(o => o.OrderId == request.OrderId);

            if (order == null)
                return ApiResponse<AsnResponse>.Failure("Order not found");

            if (order.CustomerId != customerId)
                return ApiResponse<AsnResponse>.Failure("Order does not belong to this customer");

            if (!string.Equals(order.Status, ContractSigned, StringComparison.OrdinalIgnoreCase))
                return ApiResponse<AsnResponse>.Failure("ASN can only be created after contract is signed");

            if (order.Schedule?.Route == null)
                return ApiResponse<AsnResponse>.Failure("Order has no selected route");

            var requestedDropoff = DateTime.SpecifyKind(request.RequestedDropoffTime, DateTimeKind.Unspecified);
            var now = DbNow();
            
            if (now.AddHours(6) > requestedDropoff)
            {
                return ApiResponse<AsnResponse>.Failure(
                    $"ASN must be created at least 6 hours before the requested drop-off time. Earliest allowed drop-off time is {now.AddHours(6):dd/MM/yyyy HH:mm}.");
            }

            var asnCode = await GenerateUniqueAsnCodeAsync();
            var qrValue = $"ASN|{asnCode}|ORDER|{order.OrderId}|ROUTE|{order.Schedule!.Route.RouteCode}|DROPOFF|{requestedDropoff:O}";

            var originWarehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId == request.WarehouseId);

            if (originWarehouse == null)
            {
                return ApiResponse<AsnResponse>.Failure("Warehouse not found.");
            }

            var asn = new Core.Entities.InboundAsn
            {
                AsnId = Guid.NewGuid(),
                AsnCode = asnCode,
                OrderId = order.OrderId,
                RequestedDropoffTime = requestedDropoff,
                QrCodeValue = qrValue,
                Status = "SCHEDULED",
                Phone = request.Phone,
                WarehouseId = request.WarehouseId,
                CustomerId = customerId,
                CreatedAt = DbNow()
            };

            if (!await TryCreateAsnAsync(asn))
            {
                return ApiResponse<AsnResponse>.Failure(
                    "Đơn hàng này đã có lịch giao kho.",
                    409);
            }

            try
            {
                var pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("Asn", new { Asn = asn, Order = order });
                var pdfUrl = await _fileService.UploadFileAsync(pdfBytes, $"{asnCode}.pdf");
                
                asn.FileUrl = pdfUrl;
                await _db.SaveChangesAsync();
            }
            catch (Exception)
            {
            }

            return ApiResponse<AsnResponse>.SuccessResponse(new AsnResponse
            {
                AsnId = asn.AsnId,
                AsnCode = asn.AsnCode,
                OrderId = asn.OrderId,
                RouteId = order.Schedule!.Route!.RouteId,
                RouteCode = order.Schedule!.Route.RouteCode,
                RequestedDropoffTime = asn.RequestedDropoffTime,
                CutOffTime = order.Schedule!.Route.CutOffTime,
                QrCodeValue = asn.QrCodeValue,
                Status = asn.Status,
                Phone = asn.Phone,
                WarehouseId = asn.WarehouseId,
                CustomerId = asn.CustomerId,
                WarehouseName = originWarehouse.WarehouseName,
                WarehouseAddress = originWarehouse.Address,
                FileUrl = asn.FileUrl,
                CreatedAt = asn.CreatedAt
            }, "ASN created successfully");
        }

        public async Task<ApiResponse<List<AsnScheduleResponse>>> GetScheduleAsync(
            DateOnly date,
            string? status,
            Guid? warehouseId = null)
        {
            var from = date.ToDateTime(TimeOnly.MinValue);
            var to = date.ToDateTime(TimeOnly.MaxValue);

            var query = _db.InboundAsns
                .AsNoTracking()
                .Include(a => a.Order)
                    .ThenInclude(o => o.Customer)
                .Include(a => a.Order)
                    .ThenInclude(o => o.Schedule)
                        .ThenInclude(s => s!.Route)
                .Where(a => a.RequestedDropoffTime >= from && a.RequestedDropoffTime <= to);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = status.Trim();
                query = query.Where(a => a.Status == normalizedStatus);
            }

            if (warehouseId.HasValue)
            {
                query = query.Where(a => a.WarehouseId == warehouseId.Value);
            }

            var items = await query
                .OrderBy(a => a.RequestedDropoffTime)
                .ToListAsync();

            var customerEmails = items
                .Select(a => a.Order.Customer?.Email)
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Select(email => email!.ToLower())
                .Distinct()
                .ToList();

            var customerUsers = await _db.Users
                .AsNoTracking()
                .Where(u => u.Email != null && customerEmails.Contains(u.Email.ToLower()))
                .Select(u => new { Email = u.Email!.ToLower(), u.UserId })
                .ToListAsync();

            var userByEmail = customerUsers
                .GroupBy(u => u.Email)
                .ToDictionary(g => g.Key, g => g.First().UserId);

            var result = items.Select(a =>
            {
                var customerEmail = a.Order.Customer?.Email?.ToLower();
                userByEmail.TryGetValue(customerEmail ?? string.Empty, out var customerUserId);

                return new AsnScheduleResponse
                {
                    AsnId = a.AsnId,
                    AsnCode = a.AsnCode,
                    OrderId = a.OrderId,
                    TrackingCode = a.Order.TrackingCode,
                    ItemName = a.Order.ItemName,
                    CustomerId = a.Order.CustomerId,
                    CustomerName = a.Order.Customer?.CompanyName,
                    CustomerEmail = a.Order.Customer?.Email,
                    CustomerUserId = customerUserId == Guid.Empty ? null : customerUserId,
                    RouteId = a.Order.Schedule?.RouteId,
                    RouteCode = a.Order.Schedule?.Route?.RouteCode,
                    RequestedDropoffTime = a.RequestedDropoffTime,
                    CutOffTime = a.Order.Schedule?.Route?.CutOffTime,
                    Status = a.Status,
                    QrCodeValue = a.QrCodeValue,
                    WarehouseId = a.WarehouseId
                };
            }).ToList();

            return ApiResponse<List<AsnScheduleResponse>>.SuccessResponse(result, "ASN schedule retrieved successfully");
        }

        public async Task<ApiResponse<List<AsnResponse>>> GetAsnsByCustomerIdAsync(Guid customerId)
        {
            var rawAsns = await _db.InboundAsns
                .Include(a => a.Order)
                .ThenInclude(o => o.Schedule)
                    .ThenInclude(s => s.Route)
                .Where(a => a.CustomerId == customerId || a.Order.CustomerId == customerId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.AsnId,
                    a.AsnCode,
                    a.OrderId,
                    RouteId = a.Order.Schedule != null ? a.Order.Schedule.RouteId : (Guid?)null,
                    RouteCode = (a.Order.Schedule != null && a.Order.Schedule.Route != null) ? a.Order.Schedule.Route.RouteCode : string.Empty,
                    a.RequestedDropoffTime,
                    CutOffTime = a.Order.Schedule != null ? (TimeSpan?)a.Order.Schedule.CutOffTime : null,
                    a.QrCodeValue,
                    a.Status,
                    a.Phone,
                    a.WarehouseId,
                    a.CustomerId,
                    WarehouseName = _db.Warehouses.Where(w => w.WarehouseId == a.WarehouseId).Select(w => w.WarehouseName).FirstOrDefault() ?? string.Empty,
                    WarehouseAddress = _db.Warehouses.Where(w => w.WarehouseId == a.WarehouseId).Select(w => w.Address).FirstOrDefault(),
                    a.FileUrl,
                    a.CreatedAt
                })
                .ToListAsync();

            var asns = rawAsns.Select(a => new AsnResponse
            {
                AsnId = a.AsnId,
                AsnCode = a.AsnCode,
                OrderId = a.OrderId,
                RouteId = a.RouteId ?? Guid.Empty,
                RouteCode = a.RouteCode,
                RequestedDropoffTime = a.RequestedDropoffTime,
                CutOffTime = a.CutOffTime ?? TimeSpan.Zero,
                QrCodeValue = a.QrCodeValue,
                Status = a.Status,
                Phone = a.Phone,
                WarehouseId = a.WarehouseId,
                CustomerId = a.CustomerId,
                WarehouseName = a.WarehouseName,
                WarehouseAddress = a.WarehouseAddress,
                FileUrl = a.FileUrl,
                CreatedAt = a.CreatedAt
            }).ToList();

            return ApiResponse<List<AsnResponse>>.SuccessResponse(asns, "Retrieved ASNs successfully");
        }

        private async Task<bool> TryCreateAsnAsync(Core.Entities.InboundAsn asn)
        {
            if (!string.Equals(
                    _db.Database.ProviderName,
                    "Npgsql.EntityFrameworkCore.PostgreSQL",
                    StringComparison.Ordinal))
            {
                if (await _db.InboundAsns.AnyAsync(a => a.OrderId == asn.OrderId))
                    return false;

                _db.InboundAsns.Add(asn);
                await _db.SaveChangesAsync();
                return true;
            }

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({asn.OrderId.ToString()}, 0))");

                if (await _db.InboundAsns.AnyAsync(a => a.OrderId == asn.OrderId))
                {
                    await transaction.CommitAsync();
                    return false;
                }

                _db.InboundAsns.Add(asn);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            });
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

