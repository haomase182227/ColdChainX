using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Features.Outbound.DTOs;

public class OutboundOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = null!;
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string ServiceType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
}

public class OutboundPickListDto
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string StorageLocation { get; set; } = null!;
    public int Quantity { get; set; }
    public string Condition { get; set; } = null!;
    public string Status { get; set; } = null!;
}

public class AvailableLpnDto
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid? TripId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string StorageLocation { get; set; } = null!;
    public int Quantity { get; set; }
    public string State { get; set; } = null!;
}

public class AvailableTripDto
{
    public Guid TripId { get; set; }
    public string? Status { get; set; }
    public string? Vehicle { get; set; }
    public string? Driver { get; set; }
    public DateTime? PlannedStartTime { get; set; }
    public DateTime? PlannedEndTime { get; set; }
    public decimal? EstimatedDurationHours { get; set; }
    public int TotalLpns { get; set; }
    public int LoadingCompletedLpns { get; set; }
    public bool ReadyToLoad { get; set; }
    public List<AvailableTripLpnDto> Lpns { get; set; } = new();
}

public class AvailableTripLpnDto
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public string State { get; set; } = null!;
}
