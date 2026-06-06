namespace ColdChainX.Application.DTOs
{
    public class VehicleUpdateRequest
    {
        public string? TruckPlate { get; set; }
        public string? Brand { get; set; }
        public int? ManufactureYear { get; set; }
        public string? ChassisNumber { get; set; }
        public string? EngineNumber { get; set; }
        public decimal? StandardFuelLiters { get; set; }
        public string? VehicleType { get; set; }
        public decimal? MaxWeight { get; set; }
        public decimal? MaxCbm { get; set; }
        public decimal? MinTemp { get; set; }
        public decimal? MaxTemp { get; set; }
        public string? Status { get; set; }
    }
}