using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class HoldResponseDto
    {
        public Guid HoldId { get; set; }
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string LocationCode { get; set; } = null!;
        public decimal Quantity { get; set; }
        public string ReasonCode { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime? ReleasedAt { get; set; }
        public Guid? ReleasedBy { get; set; }
        public string? ReleaseNotes { get; set; }
    }
}
