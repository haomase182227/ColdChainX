namespace ColdChainX.Application.DTOs.Asns
{
    public class AsnResponse
    {
        public Guid AsnId { get; set; }
        public string AsnCode { get; set; } = null!;
        public Guid OrderId { get; set; }
        public Guid RouteId { get; set; }
        public string RouteCode { get; set; } = null!;
        public DateTime RequestedDropoffTime { get; set; }
        public TimeSpan CutOffTime { get; set; }
        public string QrCodeValue { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? Phone { get; set; }
        public Guid? WarehouseId { get; set; }
        public Guid? CustomerId { get; set; }
        public string? FileUrl { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
