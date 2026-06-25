using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Dispatch;

// ─────────────────────────────────────────────
//  REQUEST
// ─────────────────────────────────────────────

public class PlanLoadRequest
{
    /// <summary>Danh sách LPN cần ghép chuyến (phải đang ở trạng thái IN_STOCK).</summary>
    public List<Guid> LpnIds { get; set; } = new();

    /// <summary>Xe tải được chỉ định.</summary>
    public Guid VehicleId { get; set; }

    /// <summary>LocationId của kho xuất phát (điểm đầu của lộ trình).</summary>
    public Guid OriginWarehouseLocationId { get; set; }

    /// <summary>Thời gian dự kiến xuất phát.</summary>
    public DateTime PlannedStartTime { get; set; }

    /// <summary>Thời gian dự kiến hoàn thành chuyến.</summary>
    public DateTime PlannedEndTime { get; set; }

    /// <summary>
    /// UserId của điều phối viên sẽ nhận thông báo.
    /// Nếu null, hệ thống tự tìm tất cả users có role Dispatcher.
    /// </summary>
    public Guid? DispatchCoordinatorId { get; set; }
}

// ─────────────────────────────────────────────
//  RESPONSE
// ─────────────────────────────────────────────

public class PlanLoadResult
{
    /// <summary>MasterTrip vừa được tạo.</summary>
    public Guid TripId { get; set; }

    public VehicleInfo Vehicle { get; set; } = null!;

    /// <summary>Lộ trình tổng hợp từ Goong API.</summary>
    public RouteInfo Route { get; set; } = null!;

    /// <summary>Kế hoạch xếp hàng theo thuật toán LIFO nội bộ.</summary>
    public List<LoadInstruction> LoadPlan { get; set; } = new();

    /// <summary>Lệnh điều động — thay đổi trạng thái hàng trong kho.</summary>
    public List<DispatchInstruction> DispatchInstructions { get; set; } = new();

    /// <summary>Số lượng điều phối viên đã được thông báo.</summary>
    public int NotifiedCoordinators { get; set; }
}

public class VehicleInfo
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public decimal MaxWeightKg { get; set; }
    public decimal MaxCbm { get; set; }
    public decimal TotalOrderWeightKg { get; set; }
    public decimal TotalOrderCbm { get; set; }
    public decimal WeightUtilizationPct { get; set; }
    public decimal CbmUtilizationPct { get; set; }
}

public class RouteInfo
{
    public decimal TotalDistanceKm { get; set; }
    public int TotalStops { get; set; }
    
    // Lưu tọa độ kho xuất phát để vẽ bản đồ
    public decimal OriginLat { get; set; }
    public decimal OriginLng { get; set; }
    
    public List<RouteStop> Stops { get; set; } = new();
}

public class RouteStop
{
    public int Sequence { get; set; }
    public Guid LocationId { get; set; }
    public string Address { get; set; } = null!;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    /// <summary>Khoảng cách từ điểm trước (km).</summary>
    public decimal DistanceFromPreviousKm { get; set; }

    /// <summary>Danh sách LPN sẽ được dỡ tại điểm này.</summary>
    public List<LpnSummary> LpnsToUnload { get; set; } = new();
}

public class OrderSummary
{
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal WeightKg { get; set; }
    public decimal Cbm { get; set; }
    public string TempCondition { get; set; } = null!;
}

public class LpnSummary
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string OrderTrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal WeightKg { get; set; }
    public decimal Cbm { get; set; }
    public string TempCondition { get; set; } = null!;
}

/// <summary>
/// Một mục trong kế hoạch xếp hàng. LoadOrder = 1 nghĩa là xếp LÊN ĐẦU TIÊN (nằm sâu trong xe).
/// </summary>
public class LoadInstruction
{
    /// <summary>Thứ tự xếp hàng lên xe (1 = xếp trước nhất, ở sâu nhất trong thùng).</summary>
    public int LoadOrder { get; set; }

    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public decimal WeightKg { get; set; }
    public decimal Cbm { get; set; }
    public string TempCondition { get; set; } = null!;

    /// <summary>Vị trí trong thùng xe: REAR (đuôi), MID (giữa), FRONT (đầu).</summary>
    public string Zone { get; set; } = null!;

    /// <summary>Điểm dỡ hàng tương ứng.</summary>
    public Guid DeliveryLocationId { get; set; }

    /// <summary>Thứ tự giao trong lộ trình (1 = giao đầu tiên).</summary>
    public int DeliveryStopSequence { get; set; }

    /// <summary>Lý do xếp tại vị trí này.</summary>
    public string Reason { get; set; } = null!;
}

/// <summary>Lệnh điều động — yêu cầu nhân viên kho thực hiện.</summary>
public class DispatchInstruction
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string Action { get; set; } = "LOAD"; // LOAD | STAGE
    public string PreviousStatus { get; set; } = null!;
    public string TargetStatus { get; set; } = "LOADING";
    public int LoadOrder { get; set; }
    public string Zone { get; set; } = null!;
}

// ═══════════════════════════════════════════════════════════════════════
//  API 1: AUTO-DISPATCH — Tự động ghép chuyến
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Request cho API manual-dispatch.
/// Thay thế hoàn toàn auto-dispatch. Người dùng tự chọn đơn hàng và xe.
/// </summary>
public class ManualDispatchRequest
{
    /// <summary>Danh sách LpnId do người dùng chọn (IN_STOCK).</summary>
    public List<Guid> LpnIds { get; set; } = new();

    /// <summary>VehicleId do người dùng chọn (mỗi chuyến đúng 1 xe).</summary>
    public Guid VehicleId { get; set; }

    /// <summary>Danh sách tài xế được gán cho chuyến (1–2 tài xế).</summary>
    public List<Guid> DriverIds { get; set; } = new();

    /// <summary>LocationId kho xuất phát.</summary>
    public Guid OriginWarehouseLocationId { get; set; }

    /// <summary>Thời gian dự kiến xuất phát.</summary>
    public DateTime PlannedStartTime { get; set; }

    /// <summary>Thời gian dự kiến hoàn thành chuyến.</summary>
    public DateTime PlannedEndTime { get; set; }
}

/// <summary>Form request cho manual-dispatch endpoint (multipart/form-data).</summary>
public class ManualDispatchFormRequest
{
    public string VehicleId { get; set; } = string.Empty;

    /// <summary>1–2 tài xế được gán cho chuyến (DriverId, có thể kèm hậu tố ":label").</summary>
    public List<string> DriverIds { get; set; } = new();

    public DateTime PlannedStartTime { get; set; }
    public DateTime PlannedEndTime { get; set; }
}

/// <summary>Kết quả manual-dispatch — mở rộng từ PlanLoadResult.</summary>
public class ManualDispatchResult
{
    public Guid TripId { get; set; }
    public VehicleInfo Vehicle { get; set; } = null!;

    /// <summary>Tài xế được gán cho chuyến (1–2 người). Với 2 tài xế, thời lượng chuyến được chia đều.</summary>
    public List<DriverInfo> Drivers { get; set; } = new();

    /// <summary>Tổng thời lượng lái xe ước tính của chuyến (giờ), lấy từ Goong route.</summary>
    public decimal EstimatedDurationHours { get; set; }

    /// <summary>Danh sách LPN được chọn.</summary>
    public List<LpnSummary> SelectedLpns { get; set; } = new();

    /// <summary>Lộ trình tối ưu.</summary>
    public RouteInfo Route { get; set; } = null!;

    /// <summary>Hướng dẫn đường đi từ Goong Directions API.</summary>
    public NavigationInfo Navigation { get; set; } = null!;

    /// <summary>
    /// Hướng dẫn lộ trình đầy đủ, đóng gói sẵn cho FE vẽ trực tiếp lên Goong Map:
    /// điểm xuất phát, các waypoint (marker), polyline encode + decode, và turn-by-turn.
    /// </summary>
    public RouteGuidance MapRoute { get; set; } = null!;

    /// <summary>Kế hoạch xếp hàng LIFO.</summary>
    public List<LoadInstruction> LoadPlan { get; set; } = new();

    /// <summary>Lệnh điều động.</summary>
    public List<DispatchInstruction> DispatchInstructions { get; set; } = new();
    
    public int NotifiedCoordinators { get; set; }

    /// <summary>URL đến file PDF Sơ đồ gộp chuyến (Lệnh điều động + LIFO Load Plan)</summary>
    public string? LifoPdfUrl { get; set; }

    /// <summary>Cảnh báo khi có LPN đã quá SLA deadline — không chặn dispatch.</summary>
    public string? SlaWarning { get; set; }

    /// <summary>Số LPN đã quá SLA deadline.</summary>
    public int LateLpnCount { get; set; }

    /// <summary>Tải trọng xe tối đa được khuyến nghị khi có LPN trễ SLA (kg).</summary>
    public int? SuggestedMaxPayloadKg { get; set; }
}

/// <summary>Kết quả bắt đầu picking — trip chuyển sang PICKING.</summary>
public record StartPickingResult(Guid TripId, string Status, int LpnCount);

/// <summary>
/// Kết quả hủy chuyến (cancel/un-plan). Đưa toàn bộ trạng thái về như trước khi manual-dispatch:
/// LPN → IN_STOCK (về kho), đơn hàng → IN_STOCK, xe/tài xế → ACTIVE, seal → CANCELLED.
/// </summary>
public class CancelTripResult
{
    public Guid TripId { get; set; }
    public string PreviousStatus { get; set; } = null!;
    public string NewStatus { get; set; } = "CANCELLED";

    /// <summary>Số LPN đã được đưa về IN_STOCK (trở lại kho).</summary>
    public int ResetLpnCount { get; set; }

    /// <summary>Số đơn hàng đã được đưa về IN_STOCK và gỡ khỏi chuyến.</summary>
    public int ResetOrderCount { get; set; }

    /// <summary>Số seal đã bị hủy.</summary>
    public int CancelledSealCount { get; set; }

    /// <summary>Số chứng từ (E-Waybill) đã bị vô hiệu.</summary>
    public int VoidedDocumentCount { get; set; }

    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public DateTime CancelledAt { get; set; }
    public string Message { get; set; } = null!;
}

/// <summary>Thông tin tài xế được chọn cho chuyến.</summary>
public class DriverInfo
{
    public Guid DriverId { get; set; }
    public string FullName { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string? IdentityNumber { get; set; }
    public string? LicenseClass { get; set; }
    public DateOnly? LicenseExpiry { get; set; }
    public string LicenseStatus { get; set; } = null!; // VALID, EXPIRING_SOON, EXPIRED

    /// <summary>PRIMARY (tài xế chính) hoặc SECONDARY (tài xế phụ).</summary>
    public string DriverRole { get; set; } = "PRIMARY";

    /// <summary>Số giờ lái được phân bổ cho tài xế này (EstimatedDurationHours / số tài xế).</summary>
    public decimal AssignedDurationHours { get; set; }
}

/// <summary>Hướng dẫn đường đi (Goong Directions API).</summary>
public class NavigationInfo
{
    public decimal TotalDistanceKm { get; set; }
    public int TotalDurationMinutes { get; set; }
    public string GoongRouteOverview { get; set; } = null!; // Encoded polyline overview

    /// <summary>Danh sách các bước di chuyển theo thứ tự.</summary>
    public List<NavigationLeg> Legs { get; set; } = new();
}

/// <summary>Một đoạn đường (leg) giữa 2 điểm dừng.</summary>
public class NavigationLeg
{
    public int LegIndex { get; set; }
    public string FromAddress { get; set; } = null!;
    public string ToAddress { get; set; } = null!;
    public decimal DistanceKm { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>Các bước rẽ/chỉ dẫn chi tiết trong đoạn đường này.</summary>
    public List<NavigationStep> Steps { get; set; } = new();
}

/// <summary>Một bước chỉ dẫn trong navigation (turn-by-turn).</summary>
public class NavigationStep
{
    public int StepIndex { get; set; }
    public string Instruction { get; set; } = null!; // "Rẽ phải vào đường X"
    public decimal DistanceKm { get; set; }
    public int DurationSeconds { get; set; }
    public string? Maneuver { get; set; } // "turn-right", "turn-left", "straight", etc.
}

// ─────────────────────────────────────────────
//  ROUTE GUIDANCE — gói sẵn cho FE vẽ Goong Map
// ─────────────────────────────────────────────

/// <summary>
/// Hướng dẫn lộ trình hoàn chỉnh để FE vẽ trực tiếp lên Goong Map mà không cần gọi lại API.
/// Bao gồm: điểm đầu/cuối, danh sách waypoint (marker), polyline (cả dạng encode để Goong render
/// và dạng decode lat/lng để vẽ tay), tổng quãng đường/thời gian và turn-by-turn.
/// </summary>
public class RouteGuidance
{
    public decimal OriginLat { get; set; }
    public decimal OriginLng { get; set; }
    public string OriginAddress { get; set; } = null!;

    public decimal DestinationLat { get; set; }
    public decimal DestinationLng { get; set; }
    public string DestinationAddress { get; set; } = null!;

    public decimal TotalDistanceKm { get; set; }
    public int TotalDurationMinutes { get; set; }

    /// <summary>Polyline encode (thuật toán Google/Goong) — dùng cho Goong Map render trực tiếp.</summary>
    public string? OverviewPolyline { get; set; }

    /// <summary>Các điểm marker theo đúng thứ tự lộ trình (origin → stops → destination).</summary>
    public List<RouteWaypoint> Waypoints { get; set; } = new();

    /// <summary>Polyline đã decode thành danh sách toạ độ — FE vẽ đường đi không cần thư viện decode.</summary>
    public List<RoutePathPoint> Path { get; set; } = new();

    /// <summary>Chỉ dẫn turn-by-turn gộp từ tất cả các chặng (đánh số liên tục).</summary>
    public List<NavigationStep> Steps { get; set; } = new();
}

/// <summary>Một điểm marker trên bản đồ Goong.</summary>
public class RouteWaypoint
{
    public int Sequence { get; set; }
    public string Type { get; set; } = null!; // ORIGIN | STOP | DESTINATION
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
    public string Address { get; set; } = null!;

    /// <summary>Số LPN sẽ được dỡ tại điểm này (0 với điểm xuất phát).</summary>
    public int LpnCount { get; set; }
}

/// <summary>Một toạ độ trên polyline lộ trình.</summary>
public class RoutePathPoint
{
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  API 2: WAREHOUSE ORDER — Lệnh bốc xếp cho kho
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Kết quả tạo/duyệt/từ chối lệnh kho.</summary>
public class WarehouseOrderResult
{
    public Guid TripId { get; set; }
    public string Status { get; set; } = null!; // PENDING_WH_APPROVAL, APPROVED, WH_REJECTED
    public string? RejectionReason { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public VehicleInfo? Vehicle { get; set; }

    /// <summary>Danh sách đơn hàng trong chuyến.</summary>
    public List<OrderSummary> Orders { get; set; } = new();

    /// <summary>Kế hoạch xếp hàng LIFO (gửi kèm cho kho).</summary>
    public List<LoadInstruction>? LoadPlan { get; set; }

    public int NotifiedUsers { get; set; }
}

/// <summary>Request từ chối lệnh kho.</summary>
public class RejectWarehouseOrderRequest
{
    public string Reason { get; set; } = null!;
}

// ═══════════════════════════════════════════════════════════════════════
//  API 3: IOT CHECK — Kiểm tra tín hiệu IoT xe
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Trạng thái tổng hợp IoT của xe.</summary>
public class VehicleIoTStatus
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public bool HasIoTDevices { get; set; }
    public string OverallStatus { get; set; } = null!; // ONLINE, OFFLINE, PARTIAL, NO_DEVICE

    /// <summary>Danh sách thiết bị IoT gắn trên xe.</summary>
    public List<IoTDeviceStatus> Devices { get; set; } = new();
}

/// <summary>Trạng thái chi tiết 1 thiết bị IoT.</summary>
public class IoTDeviceStatus
{
    public Guid DeviceId { get; set; }
    public int? BatteryLevel { get; set; }
    public DateTime? LastPingTime { get; set; }
    public string? Status { get; set; }
    public bool IsOnline { get; set; } // LastPingTime < 10 phút trước

    /// <summary>Dữ liệu telemetry gần nhất.</summary>
    public LatestTelemetry? LatestTelemetry { get; set; }
}

/// <summary>Dữ liệu telemetry gần nhất từ thiết bị.</summary>
public class LatestTelemetry
{
    public decimal Temperature { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  API 4: SEAL & DISPATCH — Kẹp chì + kiểm tra chất hàng
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Request kẹp chì và dispatch.</summary>
public class SealAndDispatchRequest
{
    public string SealCode { get; set; } = null!;
}

/// <summary>Kết quả kẹp chì và dispatch.</summary>
public class SealAndDispatchResult
{
    public Guid TripId { get; set; }
    public string SealCode { get; set; } = null!;
    public bool AllOrdersLoaded { get; set; }
    public int TotalOrders { get; set; }
    public int LoadedOrders { get; set; }
    public DateTime SealedAt { get; set; }
    public Guid SealedBy { get; set; }
    public string TripStatus { get; set; } = null!;

    /// <summary>URL tài liệu E-Waybill đã phát hành.</summary>
    public string? WaybillUrl { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  BACKLOG — Xử lý hàng tồn
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Request xử lý hàng tồn.</summary>
public class ProcessBacklogRequest
{
    public string OriginWarehouseLocationId { get; set; } = null!;

    /// <summary>Số ngày tồn kho tối thiểu để được xử lý. Default = 1.</summary>
    public int BacklogDays { get; set; } = 1;

    public DateTime PlannedStartTime { get; set; }
    public DateTime PlannedEndTime { get; set; }
}

/// <summary>Kết quả xử lý hàng tồn.</summary>
public class BacklogDispatchResult
{
    /// <summary>Các chuyến đã tạo cho hàng tồn.</summary>
    public List<BacklogTripSummary> DispatchedTrips { get; set; } = new();

    /// <summary>Đơn hàng không ghép được (không có xe phù hợp).</summary>
    public List<OrderSummary> SkippedOrders { get; set; } = new();

    public int TotalProcessed { get; set; }
    public int TotalSkipped { get; set; }
}

/// <summary>Tóm tắt 1 chuyến hàng tồn đã tạo.</summary>
public class BacklogTripSummary
{
    public Guid TripId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public string DriverName { get; set; } = null!;
    public int OrderCount { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string TempCondition { get; set; } = null!;
}

// ═══════════════════════════════════════════════════════════════════════
//  GOONG DIRECTIONS — Dữ liệu navigation nội bộ
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Kết quả nội bộ từ Goong Directions API.</summary>
public class GoongDirectionsResult
{
    public decimal TotalDistanceKm { get; set; }
    public int TotalDurationSeconds { get; set; }
    public string? OverviewPolyline { get; set; }
    public List<GoongLeg> Legs { get; set; } = new();
}

public class GoongLeg
{
    public decimal DistanceKm { get; set; }
    public int DurationSeconds { get; set; }
    public string? StartAddress { get; set; }
    public string? EndAddress { get; set; }
    public List<GoongStep> Steps { get; set; } = new();
}

public class GoongStep
{
    public string Instruction { get; set; } = null!;
    public decimal DistanceKm { get; set; }
    public int DurationSeconds { get; set; }
    public string? Maneuver { get; set; }
}
