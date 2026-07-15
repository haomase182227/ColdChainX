using System;

namespace ColdChainX.Application.DTOs
{
    public class VehicleDto
    {
        public Guid VehicleId { get; set; }
        public string TruckPlate { get; set; } = null!;
        public string? Brand { get; set; }
        public int? ManufactureYear { get; set; }
        public string? ChassisNumber { get; set; }
        public string? EngineNumber { get; set; }
        public decimal? StandardFuelLiters { get; set; }
        public string VehicleType { get; set; } = null!;
        public decimal MaxWeight { get; set; }
        public decimal MaxCbm { get; set; }
        public decimal? InnerLengthCm { get; set; }
        public decimal? InnerWidthCm { get; set; }
        public decimal? InnerHeightCm { get; set; }
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
