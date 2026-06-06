using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Warehouse
{
    public Guid WarehouseId { get; set; }

    public string WarehouseName { get; set; } = null!;

    public string? Address { get; set; }

    public int MaxPallets { get; set; }

    public int? CurrentPallets { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<WarehouseReceipt> WarehouseReceipts { get; set; } = new List<WarehouseReceipt>();
}
