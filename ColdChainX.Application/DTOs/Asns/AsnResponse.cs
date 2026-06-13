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
        public DateTime CutOffTime { get; set; }
        public string QrCodeValue { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }
}
