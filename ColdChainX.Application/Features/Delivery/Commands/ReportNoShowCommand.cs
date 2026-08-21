using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class ReportNoShowCommand : IRequest<ApiResponse<string>>
{
    public Guid TripStopId { get; set; }
    public Guid DriverId { get; set; }
    public IFormFile? EvidenceImageFile { get; set; }
    public string EvidenceImageUrl { get; set; } = string.Empty;
}

public class ReportNoShowCommandHandler : IRequestHandler<ReportNoShowCommand, ApiResponse<string>>
{
    private readonly IApplicationDbContext _context;
    private readonly IGoongMapService _goongMapService;
    private readonly IFileService? _fileService;

    public ReportNoShowCommandHandler(IApplicationDbContext context, IGoongMapService goongMapService, IFileService? fileService = null)
    {
        _context = context;
        _goongMapService = goongMapService;
        _fileService = fileService;
    }

    public async Task<ApiResponse<string>> Handle(ReportNoShowCommand request, CancellationToken cancellationToken)
    {
        string proofUrl = request.EvidenceImageUrl;
        if (request.EvidenceImageFile != null && _fileService != null)
        {
            proofUrl = await _fileService.UploadFileAsync(request.EvidenceImageFile);
        }

        if (string.IsNullOrWhiteSpace(proofUrl))
        {
            throw new ValidationException("Vui lòng đính kèm hình ảnh bằng chứng (EvidenceImageFile hoặc EvidenceImageUrl) xác nhận tình trạng khách hàng không xuất hiện / từ chối nhận hàng.");
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

        stop.Status = "SKIPPED_NOSHOW";
        stop.ActualDepartureTime = DateTime.UtcNow;

        stop.Note = $"{stop.Note} [No-Show Evidence: {proofUrl}]".Trim();
        _context.TripStopEvents.Add(new TripStopEvent
        {
            EventId = Guid.NewGuid(),
            StopId = stop.StopId,
            EventType = "NO_SHOW_REPORT",
            EventTime = DateTime.UtcNow,
            MetaData = $"ProofImageUrl: {proofUrl}"
        });

        if (stop.Trip?.TransportOrders != null && stop.LocationId != null)
        {
            var order = stop.Trip.TransportOrders.FirstOrDefault(o => o.DestLocation == stop.LocationId);
            if (order != null)
            {
                var existingEpod = await _context.DeliveryEpods
                    .FirstOrDefaultAsync(e => e.OrderId == order.OrderId && e.HandoverConfirmedAt != null, cancellationToken);
                if (existingEpod != null)
                    throw new ConflictException($"Order '{order.TrackingCode}' already has a completed ePOD ({existingEpod.EpodId}). Cannot report no-show again.");

                order.Status = "DELIVERY_FAILED_NOSHOW";

                var lpns = await _context.Lpns.Where(l => l.OrderId == order.OrderId).ToListAsync(cancellationToken);
                foreach (var lpn in lpns)
                {
                    lpn.State = ColdChainX.Core.Enums.LpnState.RETURN_PENDING;
                }

                var now = DateTime.UtcNow;
                _context.DeliveryEpods.Add(new DeliveryEpod
                {
                    EpodId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    CheckinTime = stop.ActualArrivalTime ?? now,
                    SignedAt = now,
                    HandoverConfirmedAt = now,
                    SignLatitude = stop.Location?.Latitude,
                    SignLongitude = stop.Location?.Longitude,
                    Status = "NO_SHOW",
                    CodAmount = 0m,
                    CodAmountPaid = 0m,
                    PaymentStatus = "SKIPPED_NO_SHOW",
                    Note = $"Customer no-show / refused to receive. Evidence: {proofUrl}",
                    CreatedAt = now
                });
            }
        }

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
                        originCoord, destCoord, waypointsCoord, cancellationToken: cancellationToken);

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
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessResponse("Báo cáo No-Show thành công với bằng chứng hình ảnh. Trạng thái trạm đã chuyển sang SKIPPED_NOSHOW và lộ trình tiếp theo đã được tối ưu.");
    }
}
