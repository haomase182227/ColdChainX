namespace ColdChainX.Application.Interfaces;

public interface IRealtimeTelemetryService
{
    Task<RealtimeGpsPosition?> GetLatestGpsPositionAsync(string deviceCode);
}

public class RealtimeGpsPosition
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
}
