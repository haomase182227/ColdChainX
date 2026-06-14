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

        public AsnService(ApplicationDbContext db)
        {
            _db = db;
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
            if (requestedDropoff.TimeOfDay > order.Route.CutOffTime)
            {
                return ApiResponse<AsnResponse>.Failure(
                    $"Requested_Dropoff_Time must be before or equal to route cut-off time {order.Route.CutOffTime:hh\\:mm\\:ss}");
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
