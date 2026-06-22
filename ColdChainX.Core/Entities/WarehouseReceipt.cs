using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class WarehouseReceipt
{
    public Guid ReceiptId { get; set; }

    public string ReceiptCode { get; set; } = null!;

    public string? ReferenceDocNo { get; set; }

    public Guid OrderId { get; set; }

    public Guid WarehouseId { get; set; }

    public string ReceiptType { get; set; } = null!;

    public string? Reason { get; set; }

    public decimal? TotalExpectedQty { get; set; }

    public decimal? TotalActualQty { get; set; }

    public decimal? RecordedTemperature { get; set; }

    public string DelivererName { get; set; } = null!;

    public Guid ReceiverId { get; set; }

    public string? Note { get; set; }

    public string? PdfUrl { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual User Receiver { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;

    public virtual ICollection<Lpn> Lpns { get; set; } = new List<Lpn>();
}
