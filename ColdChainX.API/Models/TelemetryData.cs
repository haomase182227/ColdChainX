namespace ColdChainX.API.Models;

public sealed class TelemetryData
{
    public string DeviceId { get; set; } = string.Empty;

    public double TempC { get; set; }

    public bool DoorOpen { get; set; }

    public double Lat { get; set; }

    public double Lon { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
