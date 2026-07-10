using System;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Routes
{
    public class CreateUpdateWeightTierRequest
    {
        [Required]
        public Guid RouteId { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal MinWeightKg { get; set; }

        public decimal? MaxWeightKg { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal PricePerKg { get; set; }
    }
}
