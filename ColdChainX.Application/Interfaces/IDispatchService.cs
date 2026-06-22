using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces;

public interface IDispatchService
{
    // ═══════════════════════════════════════════════════════════════════════
    //  API 1: AUTO-DISPATCH — Tự động ghép chuyến
    //  API 1: MANUAL-DISPATCH — Ghép chuyến thủ công
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thủ công chọn đơn hàng IN_WAREHOUSE,
    /// nhóm theo nhiệt độ + điểm đến, chọn xe/tài xế (giấy tờ còn hạn),
    /// tính lộ trình TSP + Goong, sinh LIFO load plan, sinh hướng dẫn navigation.
    /// </summary>
    Task<ManualDispatchResult> ManualDispatchAsync(ManualDispatchRequest request);

    // ═══════════════════════════════════════════════════════════════════════
    //  API 2: START PICKING — Bắt đầu lấy hàng từ kho
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Chuyển trip PLANNED → PICKING, thông báo loader bắt đầu lấy hàng.</summary>
    Task<StartPickingResult> StartPickingAsync(Guid tripId);

    // ═══════════════════════════════════════════════════════════════════════
    //  API 3: IOT CHECK — Kiểm tra tín hiệu IoT xe
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Kiểm tra tín hiệu IoT (GPS, nhiệt độ, battery) của xe.</summary>
    Task<VehicleIoTStatus> CheckVehicleIoTAsync(Guid vehicleId);

    // ═══════════════════════════════════════════════════════════════════════
    //  API 4: SEAL & DISPATCH — Kẹp chì + kiểm tra chất hàng
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kiểm tra tất cả đơn hàng đã chất lên xe chưa → kẹp chì → cấp E-Waybill.
    /// </summary>
    Task<SealAndDispatchResult> SealAndDispatchAsync(Guid tripId, string sealCode, Guid sealedBy);

}
