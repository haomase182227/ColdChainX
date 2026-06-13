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

    public decimal? ActualWeightKg { get; set; }

    public decimal? LengthCm { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    public string? Barcode { get; set; }

    public string? QrCode { get; set; }

    public string? BatchNumber { get; set; }

    public DateOnly? ManufacturedDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public virtual WarehouseReceipt Receipt { get; set; } = null!;
}
