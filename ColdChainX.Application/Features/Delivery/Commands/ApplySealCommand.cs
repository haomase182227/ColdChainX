using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class ApplySealCommand : IRequest<ApiResponse<ApplySealResponse>>
{
    public Guid TripId { get; set; }
    public string SealCode { get; set; } = string.Empty;
}

public class ApplySealCommandHandler : IRequestHandler<ApplySealCommand, ApiResponse<ApplySealResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IAiAlertingControlService _aiControlService;
    private readonly IMqttCommandPublisher _mqttPublisher;

    public ApplySealCommandHandler(IApplicationDbContext context, IAiAlertingControlService aiControlService, IMqttCommandPublisher mqttPublisher)
    {
        _context = context;
        _aiControlService = aiControlService;
        _mqttPublisher = mqttPublisher;
    }

    public async Task<ApiResponse<ApplySealResponse>> Handle(ApplySealCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SealCode))
        {
            throw new ValidationException("Mã chì kẹp seal mới (SealCode) không được để trống.");
        }

        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken)
            ?? throw new NotFoundException($"Chuyến xe với ID '{request.TripId}' không tồn tại trên hệ thống.");

        var newSeal = new Seal
        {
            SealId = Guid.NewGuid(),
            TripId = trip.TripId,
            StopId = null,
            SealCode = request.SealCode.Trim(),
            AppliedAt = DateTime.UtcNow,
            AppliedImageUrl = null,
            Status = "APPLIED",
            Note = "Đóng seal mới sau khi hoàn tất tháo dỡ hàng điểm trước để bảo vệ hàng ghép (LTL) tới điểm tiếp theo.",
            CreatedAt = DateTime.UtcNow
        };

        _context.Seals.Add(newSeal);

        trip.SealNumber = newSeal.SealCode;

        if (trip.VehicleId.HasValue)
        {
            var iotDevices = await _context.IotDevices
                .Where(d => d.VehicleId == trip.VehicleId.Value)
                .ToListAsync(cancellationToken);

            foreach (var device in iotDevices)
            {
                device.Status = "STREAMING";
                if (!string.IsNullOrWhiteSpace(device.DeviceCode))
                {
                    await _mqttPublisher.StartStreamingAsync(device.DeviceCode, cancellationToken);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        int coolingBufferMinutes = 15;
        string bufferReason = $"Đã đóng chì seal mới [{newSeal.SealCode}] cho chặng tiếp theo. Thiết bị IoT bắt đầu START_STREAMING, tuy nhiên hệ thống AI tạm ngừng phát cảnh báo nhiệt trong {coolingBufferMinutes} phút đầu để chờ thùng bảo ôn hạ nhiệt độ và ổn định trở lại.";
        _aiControlService.MuteTripAiAlerting(trip.TripId, TimeSpan.FromMinutes(coolingBufferMinutes), bufferReason);

        var response = new ApplySealResponse
        {
            SealId = newSeal.SealId,
            TripId = trip.TripId,
            TripCode = $"TRIP-{trip.TripId.ToString()[..8].ToUpper()}",
            SealCode = newSeal.SealCode,
            Status = "APPLIED (Đã kẹp chì / Bảo vệ chuỗi lạnh chặng kế tiếp)",
            AppliedAt = newSeal.AppliedAt.Value,
            AiAlertingRestored = true,
            AiMutedBufferMinutes = coolingBufferMinutes,
            AiMonitoringStatus = $"IoT STREAMING ON. AI Alerting sẽ chính thức phát cảnh báo trở lại sau {coolingBufferMinutes} phút nữa (Cooling Recovery Window).",
            Message = $"Đóng chì seal mới thành công, thiết bị IoT bắt đầu START_STREAMING! Hệ thống AI cho phép thời gian đệm {coolingBufferMinutes} phút đầu (không phát thông báo cảnh báo nhiệt) để nhiệt độ thùng xe sau khi đóng cửa tự động ổn định trở lại."
        };

        return ApiResponse<ApplySealResponse>.SuccessResponse(response, response.Message);
    }
}
