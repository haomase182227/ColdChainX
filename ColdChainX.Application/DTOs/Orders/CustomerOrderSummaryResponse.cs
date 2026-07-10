using System;

namespace ColdChainX.Application.DTOs.Orders
{
    public class CustomerOrderSummaryResponse
    {
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Category { get; set; } = null!;
        public int Quantity { get; set; }
        public string PackingType { get; set; } = null!;
        public string TempCondition { get; set; } = null!;
        public decimal ExpectedWeightKg { get; set; }
        public decimal ExpectedCbm { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }
}
