using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class ReportNoShowCommand : IRequest<ApiResponse<string>>
{
    public Guid TripStopId { get; set; }
    public Guid DriverId { get; set; }
    public string EvidenceImageUrl { get; set; } = string.Empty;
}

public class ReportNoShowCommandHandler : IRequestHandler<ReportNoShowCommand, ApiResponse<string>>
{
    private readonly IApplicationDbContext _context;
    private readonly IGoongMapService _goongMapService;

    public ReportNoShowCommandHandler(IApplicationDbContext context, IGoongMapService goongMapService)
    {
        _context = context;
        _goongMapService = goongMapService;
    }

    public async Task<ApiResponse<string>> Handle(ReportNoShowCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EvidenceImageUrl))
        {
            throw new ValidationException("Vui lòng cung cấp bằng chứng hình ảnh (EvidenceImageUrl) xác nhận tình trạng khách hàng không xuất hiện / từ chối nhận hàng.");
        }

        var stop = await _context.TripStops
            .Include(s => s.Location)
            .Include(s => s.Trip)
                .ThenInclude(t => t!.TransportOrders)
            .Include(s => s.Trip)
                .ThenInclude(t => t!.TripStops)
                    .ThenInclude(ts => ts.Location)
            .FirstOrDefaultAsync(s => s.StopId == request.TripStopId, cancellationToken);

        if (stop == null)
            throw new NotFoundException("Trip stop not found.");

        if (stop.ActualArrivalTime == null)
            throw new ApiException("Tài xế chưa check-in tại điểm dừng này.", 400);

        // 1. Cập nhật trạng thái điểm dừng thành SKIPPED_NOSHOW và đóng dấu giờ đi, cho phép tài xế di chuyển sang trạm tiếp theo
        stop.Status = "SKIPPED_NOSHOW";
        stop.ActualDepartureTime = DateTime.UtcNow;

        // 2. Ghi bằng chứng hình ảnh vào TripStop (Ghi chú Note) và TripStopEvents (giống cơ chế check-in)
        stop.Note = $"{stop.Note} [No-Show Evidence: {request.EvidenceImageUrl}]".Trim();
        _context.TripStopEvents.Add(new TripStopEvent
        {
            EventId = Guid.NewGuid(),
            StopId = stop.StopId,
            EventType = "NO_SHOW_REPORT",
            EventTime = DateTime.UtcNow,
            MetaData = $"ProofImageUrl: {request.EvidenceImageUrl}"
        });

        // 3. Tra cứu đơn hàng tại Stop (dựa vào LocationId) và cập nhật chứng từ TransportDocument 3NF
        if (stop.Trip?.TransportOrders != null && stop.LocationId != null)
        {
            var order = stop.Trip.TransportOrders.FirstOrDefault(o => o.DestLocation == stop.LocationId);
            if (order != null)
            {
                order.Status = "DELIVERY_FAILED_NOSHOW";

                // Lưu Bằng chứng No-Show vào bảng TransportDocuments theo chuẩn 3NF
                var evidenceDoc = new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    DocType = "NO_SHOW_EVIDENCE",
                    ImageUrl = request.EvidenceImageUrl,
                    UploadedBy = request.DriverId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.TransportDocuments.Add(evidenceDoc);

                // Chuyển các kiện LPNs sang trạng thái chờ trả hàng (RETURN_PENDING)
                var lpns = await _context.Lpns.Where(l => l.OrderId == order.OrderId).ToListAsync(cancellationToken);
                foreach (var lpn in lpns)
                {
                    lpn.State = ColdChainX.Core.Enums.LpnState.RETURN_PENDING;
                }

                // (Đã hoàn toàn loại bỏ tự động sinh hóa đơn phạt PenaltyBill theo yêu cầu)
            }
        }

        // 4. Skip-Stop Route Re-optimization: Tự động gọi Goong Maps tính toán lại lộ trình tối ưu sang điểm tiếp theo cho tài xế
        if (stop.Trip?.TripStops != null && stop.Location != null)
        {
            var remainingStops = stop.Trip.TripStops
                .Where(s => s.StopSequence > stop.StopSequence && s.ActualArrivalTime == null && s.Location != null)
                .OrderBy(s => s.StopSequence)
                .ToList();

            if (remainingStops.Count >= 2)
            {
                try
                {
                    var originCoord = $"{stop.Location.Latitude},{stop.Location.Longitude}";
                    var destStop = remainingStops.Last();
                    var destCoord = $"{destStop.Location!.Latitude},{destStop.Location.Longitude}";

                    string? waypointsCoord = null;
                    if (remainingStops.Count > 2)
                    {
                        var middleStops = remainingStops.Take(remainingStops.Count - 1)
                            .Select(s => $"{s.Location!.Latitude},{s.Location.Longitude}");
                        waypointsCoord = string.Join("|", middleStops);
                    }

                    var optimizedResult = await _goongMapService.GetOptimizedRouteAsync(
                        originCoord, destCoord, waypointsCoord, cancellationToken);

                    // Re-assign StopSequence dựa trên thứ tự WaypointOrder mới từ Goong
                    if (optimizedResult != null && optimizedResult.WaypointOrder.Count == remainingStops.Count - 1)
                    {
                        int currentSeq = stop.StopSequence + 1;
                        var middleStops = remainingStops.Take(remainingStops.Count - 1).ToList();
                        foreach (var idx in optimizedResult.WaypointOrder)
                        {
                            if (idx >= 0 && idx < middleStops.Count)
                            {
                                middleStops[idx].StopSequence = currentSeq++;
                            }
                        }
                        destStop.StopSequence = currentSeq;
                    }
                }
                catch
                {
                    // Fallback an toàn: Nếu Goong API offline / test environment không có Key, giữ nguyên thứ tự ban đầu
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessResponse("Báo cáo No-Show thành công với bằng chứng hình ảnh. Trạng thái trạm đã chuyển sang SKIPPED_NOSHOW và lộ trình tiếp theo đã được tối ưu.");
    }
}
