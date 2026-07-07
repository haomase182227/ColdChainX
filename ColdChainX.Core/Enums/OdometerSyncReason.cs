using System.Text.Json.Serialization;

namespace ColdChainX.Core.Enums;

/// <summary>
/// Lý do đồng bộ chỉ số Odometer (công tơ mét) của xe.
/// </summary>
public enum OdometerSyncReason
{
    /// <summary>
    /// ROUTINE_SYNC: Đồng bộ định kỳ (tự động qua thiết bị IoT hoặc tài xế cập nhật định kỳ giữa ca)
    /// </summary>
    ROUTINE_SYNC = 0,

    /// <summary>
    /// PRE_TRIP_INSPECTION: Kiểm tra trước chuyến đi (PTI - Pre-trip Inspection)
    /// </summary>
    PRE_TRIP_INSPECTION = 1,

    /// <summary>
    /// POST_TRIP_REPORT: Báo cáo sau chuyến đi (chốt số công tơ mét khi hoàn thành hành trình)
    /// </summary>
    POST_TRIP_REPORT = 2,

    /// <summary>
    /// MANUAL_CORRECTION: Điều chỉnh thủ công (do phát hiện sai số hoặc nhập sai)
    /// </summary>
    MANUAL_CORRECTION = 3,

    /// <summary>
    /// OTHER: Lý do khác (cần ghi chú thêm tại trường Note)
    /// </summary>
    OTHER = 4
}
