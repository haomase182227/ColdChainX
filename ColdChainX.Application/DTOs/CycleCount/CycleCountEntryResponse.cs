using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.CycleCount
{
    public class CycleCountEntryResponse
    {
        public Guid EntryId { get; set; }
        public Guid PlanId { get; set; }
        public Guid LocationId { get; set; }
        public string LocationCode { get; set; } = null!;
        public Guid? StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public Guid? BatchId { get; set; }
        public string? BatchNumber { get; set; }
        
        public decimal? SystemQuantity { get; set; }
        public int? SystemPallets { get; set; }
        
        public decimal? CountedQuantity { get; set; }
        public int? CountedPallets { get; set; }
        
        public decimal? VarianceQuantity { get; set; }
        public int? VariancePallets { get; set; }
        
        public CycleCountEntryStatus Status { get; set; }
        public DateTime? CountedAt { get; set; }
        public Guid? CountedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public Guid? ReviewedBy { get; set; }
        public string? ManagerNotes { get; set; }
        public Guid? AdjustmentId { get; set; }
    }
}
