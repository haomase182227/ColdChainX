using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model for inventory batches that are close to their expiration date.
    /// </summary>
    public class ExpiryAlertResponse
    {
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public Guid BatchId { get; set; }
        public string BatchNumber { get; set; } = null!;
        public DateOnly ExpiryDate { get; set; }
        public decimal QuantityOnHand { get; set; }
        public int RemainingDays { get; set; }
        public string WarehouseName { get; set; } = null!;
        public string LocationCode { get; set; } = null!;
    }
}
