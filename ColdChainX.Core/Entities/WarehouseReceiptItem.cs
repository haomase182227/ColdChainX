using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class WarehouseReceiptItem
{
    public Guid ItemId { get; set; }

    public Guid ReceiptId { get; set; }

    public string ItemName { get; set; } = null!;

    public string? ItemCode { get; set; }

    public string Unit { get; set; } = null!;

    public decimal ExpectedQty { get; set; }

    public decimal ActualQty { get; set; }

    public string? ConditionStatus { get; set; }

    public string? Note { get; set; }

    public virtual WarehouseReceipt Receipt { get; set; } = null!;
}
