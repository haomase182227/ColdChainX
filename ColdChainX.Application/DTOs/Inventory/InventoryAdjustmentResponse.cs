using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class InventoryAdjustmentResponse
    {
        public Guid AdjustmentId { get; set; }
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string BatchNumber { get; set; } = null!;
        public string LocationCode { get; set; } = null!;
        public InventoryAdjustmentType AdjustmentType { get; set; }
        public decimal QuantityBefore { get; set; }
        public decimal QuantityChanged { get; set; }
        public decimal QuantityAfter { get; set; }
        public int PalletsBefore { get; set; }
        public int PalletsChanged { get; set; }
        public int PalletsAfter { get; set; }
        public string ReasonNotes { get; set; } = null!;
        public InventoryAdjustmentStatus Status { get; set; }
        public Guid? MovementId { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public string CreatedByUsername { get; set; } = null!;
        public Guid? ApprovedBy { get; set; }
        public string? ApprovedByUsername { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }
    }
}
