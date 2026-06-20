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
    //  API 2: WAREHOUSE ORDER — Lệnh bốc xếp cho kho
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Tạo lệnh bốc xếp cho kho, gửi thông báo WH Monitor duyệt.</summary>
    Task<WarehouseOrderResult> CreateWarehouseOrderAsync(Guid tripId, Guid createdBy);

    /// <summary>WH Monitor duyệt lệnh → chuyển orders sang LOADING.</summary>
    Task<WarehouseOrderResult> ApproveWarehouseOrderAsync(Guid tripId, Guid approvedBy);

    /// <summary>WH Monitor từ chối lệnh → trả orders về IN_WAREHOUSE.</summary>
    Task<WarehouseOrderResult> RejectWarehouseOrderAsync(Guid tripId, Guid rejectedBy, string reason);

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

    // ═══════════════════════════════════════════════════════════════════════
    //  BACKLOG — Xử lý hàng tồn
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Quét đơn hàng tồn kho lâu (> backlogDays ngày) → ghép chuyến xe nhỏ (≤ 2000kg).
    /// </summary>
    Task<BacklogDispatchResult> ProcessBacklogOrdersAsync(
        Guid originLocationId, DateTime plannedStart, DateTime plannedEnd, int backlogDays = 1);

}
