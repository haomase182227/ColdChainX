using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services;

/// <summary>
/// Luồng 8 — Xử lý sự cố &amp; cập nhật lộ trình (Incident Routing Flow).
///
/// Bước 1 (đã có ở IncidentReportService): tài xế gửi Incident Report khẩn cấp.
/// Bước 2 (DispatchRescueAsync): điều phối viên xuất lệnh điều xe lạnh khác đến
///         hiện trường, đội bốc xếp sang toàn bộ hàng qua xe mới (Sang xe).
/// Bước 3 (tự động trong cùng lệnh): chuyến → DELAYED, tính lại ETA các trạm
///         phía trước và gửi tin nhắn xin lỗi/cập nhật cho tất cả khách hàng đang chờ.
/// </summary>
public class IncidentRescueService : IIncidentRescueService
{
    private readonly ApplicationDbContext _db;
    private readonly IGoongMapService _goongMapService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IMqttCommandPublisher _mqttPublisher;
    private readonly ILogger<IncidentRescueService> _logger;

    // Trạng thái chuyến cho phép điều xe cứu hộ — hàng phải đang trên đường
    private static readonly string[] OnRoadTripStatuses = { "SEALED", "DISPATCHED", "IN_TRANSIT", "DELAYED" };

    private const string RescueDispatchedStatus = "RESCUE_DISPATCHED";
    private const string DelayedTemplateId = "INCIDENT_TRIP_DELAYED";
    private const int DefaultTransloadMinutes = 45;
    private const decimal FallbackAvgSpeedKmh = 40m;

    public IncidentRescueService(
        ApplicationDbContext db,
        IGoongMapService goongMapService,
        IHubContext<NotificationHub> hubContext,
        IMqttCommandPublisher mqttPublisher,
        ILogger<IncidentRescueService> logger)
    {
        _db = db;
        _goongMapService = goongMapService;
        _hubContext = hubContext;
        _mqttPublisher = mqttPublisher;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  [BƯỚC 2 - LOOKUP] Danh sách xe đủ điều kiện thay thế
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<ApiResponse<List<RescueCandidateResponse>>> GetRescueCandidatesAsync(Guid incidentId)
    {
        try
        {
            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Không tìm thấy báo cáo sự cố.");
            if (!incident.TripId.HasValue)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Sự cố không gắn với chuyến hàng nào.");
            if (!incident.RequiresRescue)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Sự cố này không yêu cầu xe cứu hộ.");
            if (incident.Status == "RESOLVED")
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Sự cố đã được xử lý xong.");

            var trip = await _db.MasterTrips.FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Không tìm thấy chuyến hàng của sự cố.");

            // Tổng hàng đang trên xe (LPN ở trạng thái SHIPPING) — xe thay thế phải chở đủ
            var load = await _db.Lpns
                .Where(l => l.TripId == trip.TripId && l.State == LpnState.SHIPPING)
                .GroupBy(l => 1)
                .Select(g => new { Weight = g.Sum(l => l.ActualWeightKg), Cbm = g.Sum(l => l.ActualCbm) })
                .FirstOrDefaultAsync();
            var totalWeight = load?.Weight ?? 0m;
            var totalCbm = load?.Cbm ?? 0m;

            var vehicles = await _db.Vehicles
                .Include(v => v.IotDevices)
                .Where(v => v.Status == "ACTIVE"
                         && v.VehicleId != trip.VehicleId
                         && v.MinTemp <= trip.TargetTemperature
                         && v.MaxTemp >= trip.TargetTemperature
                         && v.MaxWeight >= totalWeight
                         && v.MaxCbm >= totalCbm
                         && v.IotDevices.Any(d => d.DeviceCode != null && d.DeviceCode != ""))
                .OrderBy(v => v.MaxWeight)
                .ToListAsync();

            var items = vehicles.Select(v => new RescueCandidateResponse
            {
                VehicleId = v.VehicleId,
                TruckPlate = v.TruckPlate,
                VehicleType = v.VehicleType,
                MaxWeight = v.MaxWeight,
                MaxCbm = v.MaxCbm,
                MinTemp = v.MinTemp,
                MaxTemp = v.MaxTemp,
                IotDeviceCount = v.IotDevices.Count(d => !string.IsNullOrWhiteSpace(d.DeviceCode)),
                OnlineIotDeviceCount = v.IotDevices.Count(d => !string.IsNullOrWhiteSpace(d.DeviceCode) && d.IsOnline),
                HasOnlineIot = v.IotDevices.Any(d => !string.IsNullOrWhiteSpace(d.DeviceCode) && d.IsOnline),
                Label = $"{v.TruckPlate} — {v.VehicleType} | tải {v.MaxWeight}kg / {v.MaxCbm}m³ | nhiệt {v.MinTemp}..{v.MaxTemp}°C | IoT {v.IotDevices.Count(d => !string.IsNullOrWhiteSpace(d.DeviceCode))}"
            }).ToList();

            return ApiResponse<List<RescueCandidateResponse>>.SuccessResponse(
                items,
                items.Count == 0
                    ? "Không có xe thay thế phù hợp"
                    : $"Tìm thấy {items.Count} xe đủ điều kiện thay thế (cần chở {totalWeight}kg / {totalCbm}m³ ở {trip.TargetTemperature}°C).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rescue candidates. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<List<RescueCandidateResponse>>.Failure($"Failed to get rescue candidates: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  [NHÁNH KHÔNG ĐỔI XE] Dispatcher xác nhận đã xử lý và cho chuyến tiếp tục
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<ApiResponse<IncidentWorkflowResult>> ContinueTripAsync(
        Guid incidentId,
        ContinueTripAfterIncidentRequest request,
        Guid dispatcherId)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.HandlingNote))
            return ApiResponse<IncidentWorkflowResult>.Failure("Handling note is required.");

        try
        {
            if (!await _db.Users.AnyAsync(u => u.UserId == dispatcherId))
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy tài khoản người xử lý.");

            var incident = await _db.IncidentReports
                .Include(i => i.Trip)
                    .ThenInclude(t => t!.Vehicle)
                .FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy báo cáo sự cố.");
            if (incident.RequiresRescue)
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Sự cố yêu cầu xe cứu hộ; hãy dùng rescue-candidates và dispatch-rescue.");
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố đã được đóng.");
            if (!incident.TripId.HasValue || incident.Trip == null || incident.Trip.Vehicle == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố không gắn với chuyến/xe hợp lệ.");

            var trip = incident.Trip;
            if (incident.Status == "CONTINUED" && trip.Status == "IN_TRANSIT")
            {
                return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                    BuildWorkflowResult(incident, trip, trip.Vehicle, incident.HandledAt ?? DbNow(),
                        "Chuyến đã được cho tiếp tục trước đó."),
                    "Trip already continued.");
            }

            if (!OnRoadTripStatuses.Contains(trip.Status))
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"Chuyến đang ở trạng thái {trip.Status ?? "UNKNOWN"} và không thể tiếp tục từ luồng sự cố.");

            var now = DbNow();
            trip.Status = "IN_TRANSIT";
            incident.Status = "CONTINUED";
            incident.HandledBy = dispatcherId;
            incident.HandledAt = now;
            incident.HandlingNote = request.HandlingNote.Trim();

            await _db.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin")
                    .SendAsync("IncidentTripContinued", new
                    {
                        incident.IncidentId,
                        trip.TripId,
                        trip.Status,
                        VehicleId = trip.Vehicle.VehicleId,
                        VehiclePlate = trip.Vehicle.TruckPlate,
                        incident.HandlingNote,
                        incident.HandledAt
                    });
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(
                    hubEx,
                    "SignalR push failed after continuing incident trip. IncidentId: {IncidentId}",
                    incidentId);
            }

            return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                BuildWorkflowResult(
                    incident,
                    trip,
                    trip.Vehicle,
                    now,
                    "Đã ghi nhận xử lý tại chỗ và cho chuyến tiếp tục."),
                "Trip continued successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to continue trip after incident. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentWorkflowResult>.Failure(
                $"Failed to continue trip after incident: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  [BƯỚC 2 + 3] Điều xe thay thế (Sang xe) → DELAYED → tính lại ETA → báo khách
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<ApiResponse<IncidentRescueResult>> DispatchRescueAsync(
        Guid incidentId, DispatchRescueRequest request, Guid dispatcherId)
    {
        if (request == null || request.ReplacementVehicleId == Guid.Empty)
            return ApiResponse<IncidentRescueResult>.Failure("Vui lòng chọn xe thay thế (ReplacementVehicleId).");

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var dispatcherExists = await _db.Users.AnyAsync(u => u.UserId == dispatcherId);
            if (!dispatcherExists)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy tài khoản điều phối viên.");

            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy báo cáo sự cố.");
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentRescueResult>.Failure("Sự cố đã được xử lý xong trước đó.");
            if (incident.Status == RescueDispatchedStatus)
                return ApiResponse<IncidentRescueResult>.Failure("Sự cố này đã có lệnh điều xe cứu hộ.");
            if (!incident.RequiresRescue)
                return ApiResponse<IncidentRescueResult>.Failure(
                    "Sự cố này không yêu cầu xe cứu hộ.");
            if (!incident.TripId.HasValue)
                return ApiResponse<IncidentRescueResult>.Failure("Sự cố không gắn với chuyến hàng nào — không thể điều xe thay thế.");

            var trip = await _db.MasterTrips
                .Include(t => t.Vehicle)
                .Include(t => t.TripStops)
                    .ThenInclude(s => s.Location)
                .FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy chuyến hàng của sự cố.");

            if (!OnRoadTripStatuses.Contains(trip.Status))
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Chuyến đang ở trạng thái {trip.Status ?? "UNKNOWN"} — chỉ điều xe thay thế khi hàng đang trên đường " +
                    $"({string.Join("/", OnRoadTripStatuses)}). Nếu chưa xuất phát, hãy dùng API hủy/ghép lại chuyến.");

            var brokenVehicle = trip.Vehicle;
            if (brokenVehicle == null)
                return ApiResponse<IncidentRescueResult>.Failure("Chuyến không có xe đang gán — dữ liệu không hợp lệ.");

            if (request.ReplacementVehicleId == brokenVehicle.VehicleId)
                return ApiResponse<IncidentRescueResult>.Failure("Xe thay thế phải khác xe đang gặp sự cố.");

            var rescueVehicle = await _db.Vehicles
                .Include(v => v.IotDevices)
                .FirstOrDefaultAsync(v => v.VehicleId == request.ReplacementVehicleId);
            if (rescueVehicle == null)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy xe thay thế.");
            if (rescueVehicle.Status != "ACTIVE")
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} đang ở trạng thái {rescueVehicle.Status ?? "UNKNOWN"} — chỉ điều được xe ACTIVE.");

            if (trip.TargetTemperature < rescueVehicle.MinTemp || trip.TargetTemperature > rescueVehicle.MaxTemp)
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} không giữ được nhiệt độ {trip.TargetTemperature}°C " +
                    $"(dải nhiệt của xe: {rescueVehicle.MinTemp}..{rescueVehicle.MaxTemp}°C).");

            var rescueDevices = rescueVehicle.IotDevices
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceCode))
                .ToList();
            if (rescueDevices.Count == 0)
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} chưa có thiết bị IoT riêng và không thể được điều cứu hộ.");

            // Toàn bộ hàng đang trên xe hỏng — phải sang hết qua xe mới
            var shippingLpns = await _db.Lpns
                .Where(l => l.TripId == trip.TripId && l.State == LpnState.SHIPPING)
                .ToListAsync();
            var totalWeight = shippingLpns.Sum(l => l.ActualWeightKg);
            var totalCbm = shippingLpns.Sum(l => l.ActualCbm);
            if (totalWeight > rescueVehicle.MaxWeight || totalCbm > rescueVehicle.MaxCbm)
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} không đủ tải để sang hàng: cần {totalWeight}kg / {totalCbm}m³, " +
                    $"xe chỉ chở tối đa {rescueVehicle.MaxWeight}kg / {rescueVehicle.MaxCbm}m³.");

            var now = DbNow();

            // ── BƯỚC 2a: xe hỏng rời chuyến → MAINTENANCE + mở phiếu sửa chữa ──
            brokenVehicle.Status = "MAINTENANCE";
            if (incident.CurrentLatitude.HasValue && incident.CurrentLongitude.HasValue)
                brokenVehicle.CurrentLocation = GoongMapService.FormatCoordinate(
                    incident.CurrentLatitude.Value, incident.CurrentLongitude.Value);

            var ticket = new MaintenanceTicket
            {
                TicketId = Guid.NewGuid(),
                TicketCode = $"MT-{DateTime.Now:yyyyMMddHHmmss}",
                VehicleId = brokenVehicle.VehicleId,
                MaintenanceType = "INCIDENT_BREAKDOWN",
                TriggeredAtOdometer = brokenVehicle.CurrentOdometer,
                GarageName = "Cứu hộ tại hiện trường",
                Description = $"Sự cố {incident.IncidentType} trên chuyến {trip.TripId}: {incident.Description}",
                IssueDate = DateOnly.FromDateTime(DateTime.Today),
                Status = "OPEN",
                CreatedBy = dispatcherId,
                CreatedAt = now
            };
            _db.MaintenanceTickets.Add(ticket);

            // ── BƯỚC 2b: gán xe thay thế vào chuyến (Sang xe — hàng giữ nguyên LPN/seal flow) ──
            trip.VehicleId = rescueVehicle.VehicleId;
            rescueVehicle.Status = "ONTRIP";

            // ── BƯỚC 3a: chuyến → DELAYED ──
            trip.Status = "DELAYED";
            incident.Status = RescueDispatchedStatus;
            incident.HandledBy = dispatcherId;
            incident.HandledAt = now;
            incident.HandlingNote = request.Note?.Trim();
            incident.BrokenVehicleId = brokenVehicle.VehicleId;
            incident.ReplacementVehicleId = rescueVehicle.VehicleId;
            incident.MaintenanceTicketId = ticket.TicketId;
            incident.RescueDispatchedAt = now;

            // ── BƯỚC 3b: tính lại ETA cho các trạm phía trước ──
            var transloadMinutes = request.TransloadMinutes is > 0 ? request.TransloadMinutes.Value : DefaultTransloadMinutes;
            var departFromScene = now.AddMinutes(transloadMinutes);

            var remainingStops = trip.TripStops
                .Where(s => s.ActualArrivalTime == null
                         && s.Status != "CANCELLED"
                         && s.Status != "COMPLETED"
                         && s.Status != "ARRIVED")
                .OrderBy(s => s.StopSequence)
                .ToList();

            var (etaMethod, stopChanges) = await RecalculateEtaAsync(incident, remainingStops, departFromScene);

            // Cập nhật giờ kết thúc dự kiến của chuyến theo trạm cuối cùng
            if (remainingStops.Count > 0)
                trip.PlannedEndTime = remainingStops[^1].PlannedDepartureTime;

            // ── BƯỚC 3c: gửi thông báo xin lỗi/cập nhật ETA cho khách hàng phía trước ──
            var tripOrders = await _db.TransportOrders
                .Include(o => o.Customer)
                .Where(o => o.MasterTripId == trip.TripId)
                .ToListAsync();

            var templateId = await GetOrCreateTemplateAsync(
                DelayedTemplateId,
                "Chuyến hàng {{tracking_code}} dự kiến trễ {{delay_minutes}} phút do sự cố vận chuyển",
                "Xe {{old_plate}} gặp sự cố ({{incident_type}}) trên đường giao hàng. " +
                "Chúng tôi đã lập tức điều xe lạnh {{new_plate}} đến thay thế để đảm bảo chất lượng hàng hóa. " +
                "Thời gian giao dự kiến mới: {{new_eta}} (kế hoạch cũ: {{old_eta}}). " +
                "Thành thật xin lỗi quý khách vì sự bất tiện này.");

            var notifiedUserIds = new List<Guid>();
            var customerUserCache = new Dictionary<Guid, Guid?>();

            foreach (var change in stopChanges)
            {
                var stop = remainingStops.First(s => s.StopId == change.StopId);
                if (!stop.LocationId.HasValue) continue;

                var stopOrders = tripOrders.Where(o => o.DestLocation == stop.LocationId.Value).ToList();
                foreach (var order in stopOrders)
                {
                    var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId, customerUserCache);
                    if (templateId == null || !customerUserId.HasValue) continue;

                    var notifParams = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        { "tracking_code", order.TrackingCode },
                        { "incident_type", incident.IncidentType },
                        { "old_plate",     brokenVehicle.TruckPlate },
                        { "new_plate",     rescueVehicle.TruckPlate },
                        { "old_eta",       FormatVnTime(change.OldEta) },
                        { "new_eta",       FormatVnTime(change.NewEta) },
                        { "delay_minutes", change.DelayMinutes.ToString(CultureInfo.InvariantCulture) }
                    });

                    _db.Notifications.Add(new Notification
                    {
                        NotiId = Guid.NewGuid(),
                        UserId = customerUserId.Value,
                        SenderId = dispatcherId,
                        TemplateId = templateId,
                        Params = notifParams,
                        OrderId = order.OrderId,
                        IsRead = false,
                        CreatedAt = now
                    });

                    change.NotifiedCustomers++;
                    notifiedUserIds.Add(customerUserId.Value);
                }
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // ── Realtime (best-effort, không block API nếu SignalR lỗi) ──
            try
            {
                // Lệnh sang xe cho đội bốc xếp + điều phối + admin
                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Loader", "Group_Admin")
                    .SendAsync("IncidentRescueDispatched", new
                    {
                        IncidentId = incident.IncidentId,
                        TripId = trip.TripId,
                        BrokenVehiclePlate = brokenVehicle.TruckPlate,
                        RescueVehiclePlate = rescueVehicle.TruckPlate,
                        Latitude = incident.CurrentLatitude,
                        Longitude = incident.CurrentLongitude,
                        TransloadLpnCount = shippingLpns.Count,
                        Note = request.Note,
                        Message = $"Sang toàn bộ {shippingLpns.Count} LPN từ xe {brokenVehicle.TruckPlate} sang xe {rescueVehicle.TruckPlate} tại hiện trường."
                    });

                // Đẩy realtime cho từng khách hàng đang chờ
                foreach (var userId in notifiedUserIds.Distinct())
                {
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("TripDelayed", new
                    {
                        TripId = trip.TripId,
                        NewVehiclePlate = rescueVehicle.TruckPlate,
                        Stops = stopChanges.Select(c => new { c.Address, c.NewEta, c.DelayMinutes })
                    });
                }
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(hubEx, "SignalR push failed after rescue dispatch. IncidentId: {IncidentId}", incidentId);
            }

            var result = new IncidentRescueResult
            {
                IncidentId = incident.IncidentId,
                IncidentStatus = incident.Status!,
                TripId = trip.TripId,
                TripStatus = trip.Status!,
                BrokenVehicleId = brokenVehicle.VehicleId,
                BrokenVehiclePlate = brokenVehicle.TruckPlate,
                BrokenVehicleStatus = brokenVehicle.Status!,
                MaintenanceTicketId = ticket.TicketId,
                RescueVehicleId = rescueVehicle.VehicleId,
                RescueVehiclePlate = rescueVehicle.TruckPlate,
                RescueVehicleStatus = rescueVehicle.Status!,
                TransloadLpnCount = shippingLpns.Count,
                EtaMethod = etaMethod,
                UpdatedStops = stopChanges,
                NotifiedCustomerCount = notifiedUserIds.Distinct().Count(),
                Message = $"Đã điều xe {rescueVehicle.TruckPlate} thay thế xe {brokenVehicle.TruckPlate} (sang {shippingLpns.Count} LPN). " +
                          $"Chuyến chuyển sang DELAYED, cập nhật ETA cho {stopChanges.Count} trạm phía trước và " +
                          $"thông báo tới {notifiedUserIds.Distinct().Count()} khách hàng."
            };

            return ApiResponse<IncidentRescueResult>.SuccessResponse(result, "Rescue vehicle dispatched successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch rescue vehicle. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentRescueResult>.Failure($"Failed to dispatch rescue vehicle: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  [XÁC NHẬN SANG HÀNG] IoT xe mới phải online, MQTT thành công rồi mới chạy
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<ApiResponse<IncidentWorkflowResult>> ConfirmTransloadAsync(
        Guid incidentId,
        ConfirmTransloadRequest request,
        Guid confirmedBy)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ConfirmationNote))
            return ApiResponse<IncidentWorkflowResult>.Failure("Confirmation note is required.");

        try
        {
            if (!await _db.Users.AnyAsync(u => u.UserId == confirmedBy))
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy người xác nhận.");

            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy báo cáo sự cố.");
            if (!incident.RequiresRescue)
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố này không có bước sang xe.");
            if (!incident.TripId.HasValue || !incident.ReplacementVehicleId.HasValue)
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố chưa có lệnh điều xe thay thế hợp lệ.");

            var trip = await _db.MasterTrips
                .Include(t => t.Vehicle)
                    .ThenInclude(v => v!.IotDevices)
                .FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null || trip.Vehicle == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy chuyến hoặc xe hiện tại.");
            if (trip.VehicleId != incident.ReplacementVehicleId)
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Xe hiện tại của chuyến không khớp xe cứu hộ đã được điều.");

            if (incident.Status == "TRANSLOAD_COMPLETED" && trip.Status == "IN_TRANSIT")
            {
                return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                    BuildWorkflowResult(
                        incident,
                        trip,
                        trip.Vehicle,
                        incident.TransloadConfirmedAt ?? DbNow(),
                        "Việc sang hàng đã được xác nhận trước đó."),
                    "Transload already confirmed.");
            }

            if (incident.Status != RescueDispatchedStatus || trip.Status != "DELAYED")
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Chỉ xác nhận sang hàng khi incident ở RESCUE_DISPATCHED và trip ở DELAYED.");

            var devices = trip.Vehicle.IotDevices
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceCode))
                .ToList();
            if (devices.Count == 0)
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Xe thay thế chưa có thiết bị IoT riêng.");

            var offlineDevices = devices.Where(d => !d.IsOnline).ToList();
            if (offlineDevices.Count > 0)
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"Thiết bị IoT chưa online: {string.Join(", ", offlineDevices.Select(d => d.DeviceCode))}. " +
                    "Chuyến vẫn ở DELAYED.");
            }

            foreach (var device in devices)
            {
                var published = await _mqttPublisher.StartStreamingAsync(
                    device.DeviceCode!,
                    CancellationToken.None);
                if (!published)
                {
                    return ApiResponse<IncidentWorkflowResult>.Failure(
                        $"Không thể bật MQTT streaming cho thiết bị {device.DeviceCode}. Chuyến vẫn ở DELAYED.");
                }
            }

            var now = DbNow();
            trip.Status = "IN_TRANSIT";
            incident.Status = "TRANSLOAD_COMPLETED";
            incident.TransloadConfirmedBy = confirmedBy;
            incident.TransloadConfirmedAt = now;
            incident.TransloadNote = request.ConfirmationNote.Trim();

            await _db.SaveChangesAsync();

            var customerUserIds = new List<Guid>();
            var cache = new Dictionary<Guid, Guid?>();
            var customerIds = await _db.TransportOrders
                .Where(o => o.MasterTripId == trip.TripId && o.CustomerId.HasValue)
                .Select(o => o.CustomerId!.Value)
                .Distinct()
                .ToListAsync();
            foreach (var customerId in customerIds)
            {
                var userId = await ResolveCustomerUserIdAsync(customerId, cache);
                if (userId.HasValue)
                    customerUserIds.Add(userId.Value);
            }

            try
            {
                var payload = new
                {
                    incident.IncidentId,
                    trip.TripId,
                    trip.Status,
                    VehicleId = trip.Vehicle.VehicleId,
                    VehiclePlate = trip.Vehicle.TruckPlate,
                    DeviceCodes = devices.Select(d => d.DeviceCode).ToArray(),
                    incident.TransloadConfirmedAt,
                    incident.TransloadNote
                };

                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin", "Group_Loader")
                    .SendAsync("IncidentTransloadCompleted", payload);
                foreach (var userId in customerUserIds)
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("TripResumed", payload);
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(
                    hubEx,
                    "SignalR push failed after transload confirmation. IncidentId: {IncidentId}",
                    incidentId);
            }

            return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                BuildWorkflowResult(
                    incident,
                    trip,
                    trip.Vehicle,
                    now,
                    "Đã xác nhận sang toàn bộ hàng, bật MQTT streaming và cho chuyến tiếp tục."),
                "Transload confirmed and trip resumed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm incident transload. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentWorkflowResult>.Failure(
                $"Failed to confirm transload: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Thuật toán tính lại ETA
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tính ETA mới cho các trạm phía trước, xuất phát từ hiện trường sự cố sau khi sang xe.
    /// Ưu tiên gọi Goong (thời gian lái thực tế), lỗi thì ước lượng theo khoảng cách
    /// Haversine với tốc độ trung bình; không có tọa độ sự cố thì dời toàn bộ lịch theo độ trễ.
    /// ETA mới không bao giờ sớm hơn kế hoạch cũ (sự cố chỉ làm trễ, không làm sớm).
    /// </summary>
    private async Task<(string EtaMethod, List<StopEtaChange> Changes)> RecalculateEtaAsync(
        IncidentReport incident,
        List<TripStop> remainingStops,
        DateTime departFromScene)
    {
        var changes = new List<StopEtaChange>();
        if (remainingStops.Count == 0)
            return ("NO_REMAINING_STOPS", changes);

        var hasCoords = incident.CurrentLatitude.HasValue
                     && incident.CurrentLongitude.HasValue
                     && remainingStops.All(s => s.Location != null);

        string etaMethod;
        var travelSeconds = new double[remainingStops.Count];

        if (hasCoords)
        {
            // Quãng đường cộng dồn từ hiện trường qua từng trạm theo thứ tự StopSequence
            var cumulativeKm = new decimal[remainingStops.Count];
            var prevLat = incident.CurrentLatitude!.Value;
            var prevLon = incident.CurrentLongitude!.Value;
            decimal cumKm = 0m;
            for (var i = 0; i < remainingStops.Count; i++)
            {
                var loc = remainingStops[i].Location!;
                cumKm += HaversineKm(prevLat, prevLon, loc.Latitude, loc.Longitude);
                cumulativeKm[i] = cumKm;
                prevLat = loc.Latitude;
                prevLon = loc.Longitude;
            }
            var totalKm = cumulativeKm[^1];

            int? goongTotalSeconds = null;
            try
            {
                var origin = GoongMapService.FormatCoordinate(incident.CurrentLatitude.Value, incident.CurrentLongitude.Value);
                var lastLoc = remainingStops[^1].Location!;
                var destination = GoongMapService.FormatCoordinate(lastLoc.Latitude, lastLoc.Longitude);
                var waypoints = string.Join("|", remainingStops
                    .Take(remainingStops.Count - 1)
                    .Select(s => GoongMapService.FormatCoordinate(s.Location!.Latitude, s.Location.Longitude)));

                var route = await _goongMapService.GetOptimizedRouteAsync(
                    origin, destination, string.IsNullOrWhiteSpace(waypoints) ? null : waypoints);
                goongTotalSeconds = route.TotalDurationSeconds;
            }
            catch (Exception goongEx)
            {
                _logger.LogWarning(goongEx, "Goong ETA recalculation failed, falling back to Haversine estimate. IncidentId: {IncidentId}", incident.IncidentId);
            }

            if (goongTotalSeconds is > 0 && totalKm > 0)
            {
                // Phân bổ tổng thời gian Goong cho từng trạm theo tỉ lệ quãng đường cộng dồn
                etaMethod = "GOONG";
                for (var i = 0; i < remainingStops.Count; i++)
                    travelSeconds[i] = (double)(cumulativeKm[i] / totalKm) * goongTotalSeconds.Value;
            }
            else
            {
                // Ước lượng theo tốc độ trung bình
                etaMethod = "HAVERSINE_FALLBACK";
                for (var i = 0; i < remainingStops.Count; i++)
                    travelSeconds[i] = (double)(cumulativeKm[i] / FallbackAvgSpeedKmh) * 3600d;
            }
        }
        else
        {
            // Không có tọa độ hiện trường — dời toàn bộ lịch còn lại theo độ trễ so với trạm kế tiếp
            etaMethod = "SHIFT_FALLBACK";
            var shift = departFromScene - remainingStops[0].PlannedArrivalTime;
            if (shift < TimeSpan.Zero) shift = TimeSpan.Zero;

            foreach (var stop in remainingStops)
            {
                var dwell = stop.PlannedDepartureTime - stop.PlannedArrivalTime;
                if (dwell < TimeSpan.Zero) dwell = TimeSpan.Zero;

                var oldEta = stop.PlannedArrivalTime;
                var newEta = oldEta + shift;

                stop.PlannedArrivalTime = newEta;
                stop.PlannedDepartureTime = newEta + dwell;

                changes.Add(BuildChange(stop, oldEta, newEta));
            }
            return (etaMethod, changes);
        }

        // Áp ETA mới = giờ rời hiện trường + thời gian di chuyển + thời gian dừng ở các trạm trước đó
        var cumulativeDwell = TimeSpan.Zero;
        foreach (var (stop, index) in remainingStops.Select((s, i) => (s, i)))
        {
            var dwell = stop.PlannedDepartureTime - stop.PlannedArrivalTime;
            if (dwell < TimeSpan.Zero) dwell = TimeSpan.Zero;

            var oldEta = stop.PlannedArrivalTime;
            var newEta = departFromScene.AddSeconds(travelSeconds[index]) + cumulativeDwell;
            if (newEta < oldEta) newEta = oldEta; // sự cố không thể làm hàng đến sớm hơn kế hoạch

            stop.PlannedArrivalTime = newEta;
            stop.PlannedDepartureTime = newEta + dwell;
            cumulativeDwell += dwell;

            changes.Add(BuildChange(stop, oldEta, newEta));
        }

        return (etaMethod, changes);
    }

    private static StopEtaChange BuildChange(TripStop stop, DateTime oldEta, DateTime newEta)
    {
        return new StopEtaChange
        {
            StopId = stop.StopId,
            StopSequence = stop.StopSequence,
            Address = stop.Location?.Address,
            OldEta = oldEta,
            NewEta = newEta,
            DelayMinutes = (int)Math.Max(0, (newEta - oldEta).TotalMinutes)
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Tìm tài khoản người dùng của khách hàng qua email (giống OrderService).</summary>
    private async Task<Guid?> ResolveCustomerUserIdAsync(Guid? customerId, Dictionary<Guid, Guid?> cache)
    {
        if (!customerId.HasValue) return null;
        if (cache.TryGetValue(customerId.Value, out var cached)) return cached;

        var customerEmail = await _db.Customers
            .Where(c => c.CustomerId == customerId.Value)
            .Select(c => c.Email)
            .FirstOrDefaultAsync();

        Guid? userId = null;
        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            userId = await _db.Users
                .Where(u => u.Email != null && u.Email.ToLower() == customerEmail.ToLower())
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
        }

        cache[customerId.Value] = userId;
        return userId;
    }

    private async Task<string?> GetOrCreateTemplateAsync(string templateId, string titleTemplate, string bodyTemplate)
    {
        var existing = await _db.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == templateId);
        if (existing != null)
        {
            existing.TitleTemplate = titleTemplate;
            existing.BodyTemplate = bodyTemplate;
            existing.Channel = "IN_APP";
            existing.Status = "ACTIVE";
            return templateId;
        }

        var msgType = await _db.Messagetypes.FirstOrDefaultAsync();
        if (msgType != null)
        {
            _db.NotificationTemplates.Add(new NotificationTemplate
            {
                TemplateId = templateId,
                TypeId = msgType.TypeId,
                TitleTemplate = titleTemplate,
                BodyTemplate = bodyTemplate,
                Channel = "IN_APP",
                Status = "ACTIVE"
            });
            return templateId;
        }

        // Fallback: dùng bất kỳ template đang active
        return await _db.NotificationTemplates
            .Where(t => t.Status == null || t.Status == "ACTIVE")
            .Select(t => t.TemplateId)
            .FirstOrDefaultAsync();
    }

    private static string FormatVnTime(DateTime value)
        => value.AddHours(7).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static IncidentWorkflowResult BuildWorkflowResult(
        IncidentReport incident,
        MasterTrip trip,
        Vehicle vehicle,
        DateTime confirmedAt,
        string message)
    {
        return new IncidentWorkflowResult
        {
            IncidentId = incident.IncidentId,
            IncidentStatus = incident.Status ?? "UNKNOWN",
            TripId = trip.TripId,
            TripStatus = trip.Status ?? "UNKNOWN",
            VehicleId = vehicle.VehicleId,
            VehiclePlate = vehicle.TruckPlate,
            ConfirmedAt = confirmedAt,
            Message = message
        };
    }

    private static decimal HaversineKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = ToRad((double)(lat2 - lat1));
        var dLon = ToRad((double)(lon2 - lon1));
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad((double)lat1)) * Math.Cos(ToRad((double)lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return (decimal)(earthRadiusKm * c);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
