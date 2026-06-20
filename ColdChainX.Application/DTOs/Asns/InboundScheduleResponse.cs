using System;

namespace ColdChainX.Application.DTOs.Asns
{
    public class InboundScheduleResponse
    {
        public Guid AsnId { get; set; }
        public string AsnCode { get; set; } = null!;
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Category { get; set; } = null!;
        public int Quantity { get; set; }
        public string TempCondition { get; set; } = null!;
        public decimal ExpectedWeightKg { get; set; }
        public decimal ExpectedCbm { get; set; }
        public string DestAddress { get; set; } = null!;
        public DateTime RequestedDropoffTime { get; set; }
        public string Status { get; set; } = null!;
        public string QrCodeValue { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
        
        // Matched Warehouse Info
        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
    }
}
