using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class InventoryBatch
{
    public Guid BatchId { get; set; }
    public string ItemCode { get; set; } = null!;
    public string BatchNumber { get; set; } = null!;
    public DateOnly? ManufacturedDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public virtual ICollection<InventoryStock> InventoryStocks { get; set; } = new List<InventoryStock>();
}
