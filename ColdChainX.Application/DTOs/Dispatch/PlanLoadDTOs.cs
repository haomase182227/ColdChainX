using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Dispatch;

// ─────────────────────────────────────────────
//  REQUEST
// ─────────────────────────────────────────────

public class PlanLoadRequest
{
    /// <summary>Danh sách đơn hàng cần ghép chuyến (phải đang ở trạng thái IN_WAREHOUSE).</summary>
    public List<Guid> OrderIds { get; set; } = new();

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

    /// <summary>Danh sách đơn hàng sẽ được dỡ tại điểm này.</summary>
    public List<OrderSummary> OrdersToUnload { get; set; } = new();
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

/// <summary>
/// Một mục trong kế hoạch xếp hàng. LoadOrder = 1 nghĩa là xếp LÊN ĐẦU TIÊN (nằm sâu trong xe).
/// </summary>
public class LoadInstruction
{
    /// <summary>Thứ tự xếp hàng lên xe (1 = xếp trước nhất, ở sâu nhất trong thùng).</summary>
    public int LoadOrder { get; set; }

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
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string Action { get; set; } = "LOAD"; // LOAD | STAGE
    public string PreviousStatus { get; set; } = null!;
    public string TargetStatus { get; set; } = "LOADING";
    public int LoadOrder { get; set; }
    public string Zone { get; set; } = null!;
}
