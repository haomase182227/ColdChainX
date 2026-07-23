using System;
namespace ColdChainX.Application.DTOs.Delivery;

public class MarkFailedDeliveryRequest
{
    public string Reason { get; set; } = null!;
}