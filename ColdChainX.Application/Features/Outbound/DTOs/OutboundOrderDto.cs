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

/// <summary>
/// LPN dang o trang thai LOADING — san sang de goi POST /api/Outbound/pick.
/// </summary>
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

/// <summary>
/// Chuyen dang o trang thai PICKING kem tat ca LPN/don hang —
/// dung de chuan bi du lieu cho POST /api/Outbound/pick va POST /api/Outbound/load-trip.
/// </summary>
public class AvailableTripDto
{
    public Guid TripId { get; set; }
    public string? Status { get; set; }
    public int TotalLpns { get; set; }
    public int LoadingCompletedLpns { get; set; }
    /// <summary>True khi tat ca LPN da LOADING_COMPLETED — co the goi load-trip.</summary>
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
