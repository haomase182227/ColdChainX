using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class DepartResponse
{
    public Guid StopId { get; set; }
    public DateTime DepartTime { get; set; }
    public string? NewSealCode { get; set; }
    public bool TripCompleted { get; set; }
}
