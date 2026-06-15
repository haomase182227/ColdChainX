using System;

namespace ColdChainX.Application.DTOs.Outbound
{
    public class AllocationItemDto
    {
        public Guid AllocationId { get; set; }
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string BatchNumber { get; set; } = null!;
        public DateOnly ExpiryDate { get; set; }
        public string LocationCode { get; set; } = null!;
        public string ZoneCode { get; set; } = null!;
        public decimal AllocatedQuantity { get; set; }
        public decimal AvailableQuantity { get; set; }
        public string Status { get; set; } = null!;
    }
}
