namespace ColdChainX.Application.Interfaces;

/// <summary>
/// Cung cấp tọa độ GPS real-time mới nhất từ nguồn hot cache (Redis) thay vì SQL Database.
/// Sử dụng cho Check-in và các nghiệp vụ cần vị trí tức thời với độ trễ tối thiểu.
/// </summary>
public interface IRealtimeTelemetryService
{
    /// <summary>
    /// Lấy tọa độ GPS mới nhất của thiết bị IoT từ Redis cache.
    /// Trả về null nếu không có dữ liệu hoặc Redis không khả dụng.
    /// </summary>
    /// <param name="deviceCode">Mã thiết bị IoT (ví dụ: ESP32-SEED-102)</param>
    /// <returns>Tọa độ (Latitude, Longitude) và timestamp, hoặc null nếu không có.</returns>
    Task<RealtimeGpsPosition?> GetLatestGpsPositionAsync(string deviceCode);
}

/// <summary>
/// Vị trí GPS real-time từ thiết bị IoT.
/// </summary>
public class RealtimeGpsPosition
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
}
