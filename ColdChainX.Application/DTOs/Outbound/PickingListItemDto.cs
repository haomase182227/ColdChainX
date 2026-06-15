using System;

namespace ColdChainX.Application.DTOs.Outbound
{
    public class PickingListItemDto
    {
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public Guid LocationId { get; set; }
        public string LocationCode { get; set; } = null!;
        public string ZoneCode { get; set; } = null!;
        public string BatchNumber { get; set; } = null!;
        public DateOnly ExpiryDate { get; set; }
        public decimal QuantityToPick { get; set; }
    }
}
