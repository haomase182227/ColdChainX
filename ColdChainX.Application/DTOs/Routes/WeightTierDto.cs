using System;

namespace ColdChainX.Application.DTOs.Routes
{
    public class WeightTierDto
    {
        public Guid Id { get; set; }
        public Guid RouteId { get; set; }
        public decimal MinWeightKg { get; set; }
        public decimal? MaxWeightKg { get; set; }
        public decimal PricePerKg { get; set; }
    }
}
