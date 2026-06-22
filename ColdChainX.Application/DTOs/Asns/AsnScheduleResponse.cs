namespace ColdChainX.Application.DTOs.Asns;

public class AsnScheduleResponse
{
    public Guid AsnId { get; set; }

    public string AsnCode { get; set; } = null!;

    public Guid OrderId { get; set; }

    public string? TrackingCode { get; set; }

    public Guid? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerEmail { get; set; }

    public Guid? CustomerUserId { get; set; }

    public Guid? RouteId { get; set; }

    public string? RouteCode { get; set; }

    public DateTime RequestedDropoffTime { get; set; }

    public TimeSpan? CutOffTime { get; set; }

    public string Status { get; set; } = null!;

    public string QrCodeValue { get; set; } = null!;
}
