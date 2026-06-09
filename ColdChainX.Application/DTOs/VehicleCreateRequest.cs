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
        public string VehicleType { get; set; } = null!;
        public decimal MaxWeight { get; set; }
        public decimal MaxCbm { get; set; }
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public string? Status { get; set; }
    }
}