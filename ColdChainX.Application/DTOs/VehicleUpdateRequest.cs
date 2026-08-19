namespace ColdChainX.Application.DTOs
{
    /// <summary>
    /// Cập nhật xe; ba kích thước lòng thùng dùng đơn vị centimet (cm).
    /// Khi kích thước thay đổi, Max CBM được hệ thống tự tính lại.
    /// </summary>
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

        /// <summary>Chiều dài lòng thùng, đơn vị centimet (cm).</summary>
        public decimal? InnerLengthCm { get; set; }

        /// <summary>Chiều rộng lòng thùng, đơn vị centimet (cm).</summary>
        public decimal? InnerWidthCm { get; set; }

        /// <summary>Chiều cao lòng thùng, đơn vị centimet (cm).</summary>
        public decimal? InnerHeightCm { get; set; }
        public decimal? MinTemp { get; set; }
        public decimal? MaxTemp { get; set; }
        public string? Status { get; set; }
    }
}
