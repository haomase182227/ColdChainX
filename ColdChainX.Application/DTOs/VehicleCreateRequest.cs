using ColdChainX.Core.Enums;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs
{
    public class VehicleCreateRequest
    {
        public string TruckPlate { get; set; } = null!;
        public string? Brand { get; set; }
        public int? ManufactureYear { get; set; }
        public string? ChassisNumber { get; set; }
        public string? EngineNumber { get; set; }
        public decimal? StandardFuelLiters { get; set; }

        public VehicleType VehicleType { get; set; }

        public decimal MaxWeight { get; set; }
        public decimal MaxCbm { get; set; }
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }

        public VehicleStatus Status { get; set; } = VehicleStatus.Active;

        public IFormFile? VehicleImage { get; set; }
    }
}
