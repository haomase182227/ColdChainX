using System;
namespace ColdChainX.Application.DTOs.Delivery;

public class UpdateLocationRequest
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}