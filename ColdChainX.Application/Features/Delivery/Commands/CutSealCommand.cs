using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class CutSealCommand : IRequest<ApiResponse<CutSealResponse>>
{
    public Guid TripId { get; set; }
    public Guid? StopId { get; set; }
}

public class CutSealCommandHandler : IRequestHandler<CutSealCommand, ApiResponse<CutSealResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IAiAlertingControlService _aiControlService;
    private readonly IMqttCommandPublisher _mqttPublisher;

    public CutSealCommandHandler(IApplicationDbContext context, IAiAlertingControlService aiControlService, IMqttCommandPublisher mqttPublisher)
    {
        _context = context;
        _aiControlService = aiControlService;
        _mqttPublisher = mqttPublisher;
    }

    public async Task<ApiResponse<CutSealResponse>> Handle(CutSealCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken)
            ?? throw new NotFoundException($"Chuyến xe với ID '{request.TripId}' không tồn tại trên hệ thống.");

        // Tìm seal đang hoạt động (Status != "CUT" và RemovedAt == null) của chuyến xe
        var activeSeal = await _context.Seals
            .Where(s => s.TripId == request.TripId && s.RemovedAt == null && s.Status != "CUT" && s.Status != "REMOVED")
            .OrderByDescending(s => s.AppliedAt ?? s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeSeal == null)
        {
            // Nếu không tìm thấy seal trong DB, nhưng Trip có SealNumber, ta tự động tạo bản ghi seal đã cắt để lưu dấu vết audit
            activeSeal = new Core.Entities.Seal
            {
                SealId = Guid.NewGuid(),
                TripId = trip.TripId,
                StopId = request.StopId,
                SealCode = !string.IsNullOrEmpty(trip.SealNumber) ? trip.SealNumber : "SEAL-" + trip.TripId.ToString()[..6].ToUpper(),
                AppliedAt = DateTime.UtcNow.AddHours(-2),
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            };
            _context.Seals.Add(activeSeal);
        }

        activeSeal.Status = "CUT";
        activeSeal.RemovedAt = DateTime.UtcNow;
        activeSeal.Note = "Cắt seal tại bãi đỗ / điểm dừng để mở cửa xe dỡ hàng LIFO.";
        if (request.StopId.HasValue)
        {
            activeSeal.StopId = request.StopId;
        }

        // Cập nhật trạng thái chì trên Trip
        trip.SealNumber = $"{activeSeal.SealCode} (ĐÃ CẮT / UNSEALED)";

        // Chuyển các thiết bị IoT trên xe về trạng thái Stream off để ngừng gửi tín hiệu liên tục khi mở cửa dỡ hàng
        if (trip.VehicleId.HasValue)
        {
            var iotDevices = await _context.IotDevices
                .Where(d => d.VehicleId == trip.VehicleId.Value)
                .ToListAsync(cancellationToken);

            foreach (var device in iotDevices)
            {
                device.Status = "STREAM_OFF";
                if (!string.IsNullOrWhiteSpace(device.DeviceCode))
                {
                    await _mqttPublisher.StopStreamingAsync(device.DeviceCode, cancellationToken);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // TẮT CẢNH BÁO AI (Mute AI Alerts) để khi mở cửa xe dỡ hàng, AI không gửi liên tục các cảnh báo nhiệt độ / mở cửa
        int muteHours = 3;
        string muteReason = $"Đã cắt chì seal [{activeSeal.SealCode}] để mở cửa xe dỡ hàng. Hệ thống AI tự động tạm tắt cảnh báo nhiệt độ & cửa mở trong {muteHours} giờ.";
        _aiControlService.MuteTripAiAlerting(trip.TripId, TimeSpan.FromHours(muteHours), muteReason);

        var response = new CutSealResponse
        {
            SealId = activeSeal.SealId,
            TripId = trip.TripId,
            TripCode = $"TRIP-{trip.TripId.ToString()[..8].ToUpper()}",
            SealCode = activeSeal.SealCode,
            Status = "CUT (Đã cắt chì / Mở cửa dỡ hàng)",
            RemovedAt = activeSeal.RemovedAt.Value,
            AiAlertingMuted = true,
            AiMutedReason = muteReason,
            MutedDurationHours = muteHours
        };

        return ApiResponse<CutSealResponse>.SuccessResponse(response, "Cắt chì seal thành công. Hệ thống AI theo dõi cảnh báo đã được tự động tắt cho đến khi hoàn tất dỡ hàng.");
    }
}
