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
        if (request.EvidenceImageFile == null && string.IsNullOrWhiteSpace(request.EvidenceImageUrl))
        {
            throw new ValidationException("Vui lòng đính kèm hình ảnh bằng chứng (EvidenceImageFile hoặc EvidenceImageUrl) xác nhận tình trạng khách hàng không xuất hiện / từ chối nhận hàng.");
        }

        var stop = await _context.TripStops
            .Include(s => s.Location)
            .Include(s => s.Trip)
                .ThenInclude(t => t!.TripStops)
                    .ThenInclude(ts => ts.Location)
            .FirstOrDefaultAsync(s => s.StopId == request.TripStopId, cancellationToken);

        if (stop == null)
            throw new NotFoundException("Trip stop not found.");

        if (stop.ActualArrivalTime == null)
            throw new ApiException("Tài xế chưa check-in tại điểm dừng này.", 400);

        var reporter = await _context.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.UserId == request.DriverId, cancellationToken);
        if (reporter == null)
            throw new ForbiddenException("Không tìm thấy tài khoản đang báo khách vắng mặt.");

        if (reporter.Role?.RoleName.Equals("Driver", StringComparison.OrdinalIgnoreCase) == true)
        {
            var assignedToTrip = await _context.TripDrivers
                .AnyAsync(
                    tripDriver => tripDriver.TripId == stop.TripId
                                  && tripDriver.Driver.UserId == request.DriverId,
                    cancellationToken);
            if (!assignedToTrip)
                throw new ForbiddenException("Bạn không phải tài xế được phân công cho chuyến này.");
        }

        var noShowOrders = await _context.TransportOrders
            .Where(order =>
                (order.MasterTripId == stop.TripId
                 || _context.Lpns.Any(lpn =>
                     lpn.TripId == stop.TripId && lpn.OrderId == order.OrderId))
                && (order.DropoffStopId == stop.StopId
                    || (stop.LocationId.HasValue
                        && order.DestLocation == stop.LocationId.Value)))
            .ToListAsync(cancellationToken);
        if (noShowOrders.Count == 0)
            throw new ValidationException("Điểm dừng này không có đơn hàng nào để ghi nhận khách vắng mặt.");

        var noShowOrderIds = noShowOrders.Select(order => order.OrderId).ToList();
        var existingEpod = await _context.DeliveryEpods
            .FirstOrDefaultAsync(
                epod => epod.OrderId.HasValue
                        && noShowOrderIds.Contains(epod.OrderId.Value)
                        && epod.HandoverConfirmedAt != null,
                cancellationToken);
        if (existingEpod != null)
        {
            var completedOrder = noShowOrders.First(order => order.OrderId == existingEpod.OrderId);
            throw new ConflictException(
                $"Order '{completedOrder.TrackingCode}' already has a completed ePOD ({existingEpod.EpodId}). Cannot report no-show again.");
        }

        string proofUrl = request.EvidenceImageUrl;
        if (request.EvidenceImageFile != null && _fileService != null)
            proofUrl = await _fileService.UploadFileAsync(request.EvidenceImageFile);
        if (string.IsNullOrWhiteSpace(proofUrl))
        {
            throw new ValidationException("Không thể lưu ảnh bằng chứng khách vắng mặt. Vui lòng thử lại.");
        }

        var now = DateTime.UtcNow;
        stop.Status = "SKIPPED_NOSHOW";
        stop.ActualDepartureTime = now;

        stop.Note = $"{stop.Note} [No-Show Evidence: {proofUrl}]".Trim();
        _context.TripStopEvents.Add(new TripStopEvent
        {
            EventId = Guid.NewGuid(),
            StopId = stop.StopId,
            EventType = "NO_SHOW_REPORT",
            EventTime = now,
            MetaData = $"ProofImageUrl: {proofUrl}"
        });

        var lpns = await _context.Lpns
            .Where(lpn => noShowOrderIds.Contains(lpn.OrderId))
            .ToListAsync(cancellationToken);
        foreach (var order in noShowOrders)
        {
            order.Status = "DELIVERY_FAILED_NOSHOW";

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

        foreach (var lpn in lpns)
            lpn.State = ColdChainX.Core.Enums.LpnState.RETURN_PENDING;

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
