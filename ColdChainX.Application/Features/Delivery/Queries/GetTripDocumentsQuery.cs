using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Queries;

public class GetTripDocumentsQuery : IRequest<ApiResponse<TripDocumentsResponse>>
{
    public Guid TripId { get; set; }
    public Guid? StopId { get; set; }
    public Guid? CustomerId { get; set; }
}

public class GetTripDocumentsQueryHandler : IRequestHandler<GetTripDocumentsQuery, ApiResponse<TripDocumentsResponse>>
{
    private readonly IApplicationDbContext _context;

    public GetTripDocumentsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<TripDocumentsResponse>> Handle(GetTripDocumentsQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.TripStops)
            .ThenInclude(ts => ts.Location)
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken)
            ?? throw new NotFoundException($"Chuyến xe với ID '{request.TripId}' không tồn tại trên hệ thống.");

        var vehicle = trip.VehicleId.HasValue 
            ? await _context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == trip.VehicleId.Value, cancellationToken)
            : null;
        string vehiclePlate = vehicle?.TruckPlate ?? "N/A";

        string tripCodeStr = $"TRIP-{trip.TripId.ToString()[..8].ToUpper()}";
        string stopAddress = "Toàn bộ tuyến / Tất cả điểm dừng";

        var ordersQuery = _context.TransportOrders
            .Include(o => o.DestLocationNavigation)
            .Where(o => o.MasterTripId == trip.TripId);

        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            ordersQuery = ordersQuery.Where(o => o.CustomerId == request.CustomerId.Value);
        }

        if (request.StopId.HasValue && request.StopId != Guid.Empty)
        {
            var stop = trip.TripStops.FirstOrDefault(s => s.StopId == request.StopId.Value)
                       ?? throw new NotFoundException($"Điểm dừng với ID '{request.StopId}' không tồn tại trong lộ trình chuyến xe.");
            
            stopAddress = stop.Location?.Address ?? $"Điểm dừng {stop.StopSequence}";
            ordersQuery = ordersQuery.Where(o => o.DestLocation == stop.LocationId);
        }

        var customerOrders = await ordersQuery.ToListAsync(cancellationToken);
        if (customerOrders.Count == 0 && request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng nào thuộc sở hữu của Quý khách trên chuyến xe này.");
        }

        // Tự động suy luận thông tin điểm hạ hàng (Address) từ chính đơn hàng của Khách (kể cả khi không cần truyền StopId)
        if ((!request.StopId.HasValue || request.StopId == Guid.Empty) && customerOrders.Any())
        {
            var destAddresses = customerOrders
                .Select(o => o.DestLocationNavigation?.Address ?? trip.TripStops.FirstOrDefault(s => s.LocationId == o.DestLocation)?.Location?.Address)
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct()
                .ToList();

            if (destAddresses.Any())
            {
                stopAddress = string.Join(" | ", destAddresses!);
            }
        }

        var orderIds = customerOrders.Select(o => o.OrderId).ToList();

        string tripIdStr = trip.TripId.ToString();
        var rawDocs = await _context.TransportDocuments
            .Where(d => (d.OrderId.HasValue && orderIds.Contains(d.OrderId.Value)) || (d.ImageUrl != null && d.ImageUrl.Contains(tripIdStr)))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var documentItems = rawDocs.Select(d =>
        {
            var docType = string.IsNullOrEmpty(d.DocType) ? "E_WAYBILL" : d.DocType;
            string desc = (d.ImageUrl != null && d.ImageUrl.Contains(tripIdStr)) || !d.OrderId.HasValue
                ? docType switch
                {
                    "LIFO-PLAN" => $"Sơ đồ xếp dỡ LIFO thuộc chuyến {tripCodeStr}",
                    "MANIFEST" => $"Biên bản hàng ghép (Manifest) thuộc chuyến {tripCodeStr}",
                    "OUTBOUND-TICKET" => $"Phiếu xuất kho thuộc chuyến {tripCodeStr}",
                    "E-WAYBILL" or "E_WAYBILL" => $"Giấy đi đường E-Waybill thuộc chuyến {tripCodeStr}",
                    _ => $"Chứng từ ({docType}) thuộc chuyến {tripCodeStr}"
                }
                : $"Chứng từ ({docType}) thuộc đơn hàng {(d.OrderId.HasValue && d.OrderId.Value.ToString().Length >= 8 ? d.OrderId.Value.ToString()[..8].ToUpper() : "N/A")}";

            return new ManifestDocumentItem
            {
                DocId = d.DocId,
                OrderId = d.OrderId,
                DocType = docType,
                ImageUrl = d.ImageUrl ?? string.Empty,
                Description = desc,
                CreatedAt = d.CreatedAt
            };
        }).ToList();

        var response = new TripDocumentsResponse
        {
            TripId = trip.TripId,
            TripCode = tripCodeStr,
            StopAddress = stopAddress,
            VehiclePlate = vehiclePlate,
            TemperatureRange = "2°C - 8°C (Bảo quản lạnh tiêu chuẩn)",
            TotalCustomerOrders = customerOrders.Count,
            TotalDocuments = documentItems.Count,
            Documents = documentItems
        };

        return ApiResponse<TripDocumentsResponse>.SuccessResponse(response, "Tra cứu danh sách chứng từ giao nhận dành cho Khách hàng thành công.");
    }
}
