namespace ColdChainX.Core.Enums;

/// <summary>
/// Trạng thái vòng đời của một LPN (License Plate Number) trong hệ thống.
///
/// Luồng xuất kho (Outbound flow):
///   IN_STOCK
///     ↓  [POST /api/Dispatch/manual-dispatch]       — ghép chuyến
///   ALLOCATED
///     ↓  [POST /api/Dispatch/trip/{id}/start-picking] — bắt đầu lệnh bốc hàng
///   LOADING
///     ↓  [POST /api/Outbound/pick]                  — xác nhận từng LPN đã bốc
///   LOADING_COMPLETED
///     ↓  [POST /api/Outbound/load-trip]             — xác nhận toàn bộ chuyến đã lên xe
///   RELEASED
///     ↓  [POST /api/Dispatch/seal-and-dispatch/{id}] — kẹp chì + cấp giấy đi đường
///   SHIPPING
/// </summary>
public enum LpnState
{
    EXPECTED          = 0,
    RECEIVING         = 1,
    DISCREPANCY_HOLD  = 2,
    RETURN_PENDING    = 3,
    IN_STOCK          = 4,
    ALLOCATED         = 5,
    LOADING           = 6,
    LOADING_COMPLETED = 9,
    RELEASED          = 7,
    SHIPPING          = 8,
    DELETED           = 10
}
