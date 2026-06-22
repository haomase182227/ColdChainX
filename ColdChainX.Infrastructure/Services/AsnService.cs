using ColdChainX.Application.DTOs.Asns;
using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.EntityFrameworkCore;

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
                Phone = request.Phone,
                WarehouseId = request.WarehouseId,
                CustomerId = request.CustomerId ?? customerId,
                CreatedAt = DbNow()
            };

            _db.InboundAsns.Add(asn);
            await _db.SaveChangesAsync();

            try
            {
                // Generate PDF and Upload
                var pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("Asn", new { Asn = asn, Order = order });
                var pdfUrl = await _fileService.UploadFileAsync(pdfBytes, $"{asnCode}.pdf");
                
                asn.FileUrl = pdfUrl;
                await _db.SaveChangesAsync();
            }
            catch (Exception)
            {
                // Ignore PDF gen error for now so we don't break ASN creation
            }

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
                Phone = asn.Phone,
                WarehouseId = asn.WarehouseId,
                CustomerId = asn.CustomerId,
                FileUrl = asn.FileUrl,
                CreatedAt = asn.CreatedAt
            }, "ASN created successfully");
        }

        public async Task<ApiResponse<List<AsnScheduleResponse>>> GetScheduleAsync(DateOnly date, string? status)
        {
            var from = date.ToDateTime(TimeOnly.MinValue);
            var to = date.ToDateTime(TimeOnly.MaxValue);

            var query = _db.InboundAsns
                .AsNoTracking()
                .Include(a => a.Order)
                    .ThenInclude(o => o.Customer)
                .Include(a => a.Order)
                    .ThenInclude(o => o.Route)
                .Where(a => a.RequestedDropoffTime >= from && a.RequestedDropoffTime <= to);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = status.Trim();
                query = query.Where(a => a.Status == normalizedStatus);
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
                    CustomerId = a.Order.CustomerId,
                    CustomerName = a.Order.Customer?.CompanyName,
                    CustomerEmail = a.Order.Customer?.Email,
                    CustomerUserId = customerUserId == Guid.Empty ? null : customerUserId,
                    RouteId = a.Order.RouteId,
                    RouteCode = a.Order.Route?.RouteCode,
                    RequestedDropoffTime = a.RequestedDropoffTime,
                    CutOffTime = a.Order.Route?.CutOffTime,
                    Status = a.Status,
                    QrCodeValue = a.QrCodeValue
                };
            }).ToList();

            return ApiResponse<List<AsnScheduleResponse>>.SuccessResponse(result, "ASN schedule retrieved successfully");
        }

        public async Task<ApiResponse<List<AsnResponse>>> GetAsnsByCustomerIdAsync(Guid customerId)
        {
            var rawAsns = await _db.InboundAsns
                .Include(a => a.Order)
                .ThenInclude(o => o.Route)
                .Where(a => a.CustomerId == customerId || a.Order.CustomerId == customerId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.AsnId,
                    a.AsnCode,
                    a.OrderId,
                    RouteId = a.Order.RouteId,
                    RouteCode = a.Order.Route != null ? a.Order.Route.RouteCode : string.Empty,
                    a.RequestedDropoffTime,
                    CutOffTime = a.Order.Route != null ? (TimeSpan?)a.Order.Route.CutOffTime : null,
                    a.QrCodeValue,
                    a.Status,
                    a.Phone,
                    a.WarehouseId,
                    a.CustomerId,
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
                FileUrl = a.FileUrl,
                CreatedAt = a.CreatedAt
            }).ToList();

            return ApiResponse<List<AsnResponse>>.SuccessResponse(asns, "Retrieved ASNs successfully");
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
