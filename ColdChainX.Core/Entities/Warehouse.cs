using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Warehouse
{
    public Guid WarehouseId { get; set; }

    public string WarehouseName { get; set; } = null!;

    public string? Address { get; set; }

    public string WarehouseCode { get; set; } = null!;

    public string WarehouseType { get; set; } = null!;

    public decimal? DefaultMinTemp { get; set; }

    public decimal? DefaultMaxTemp { get; set; }

    public int MaxPallets { get; set; }

    public int? CurrentPallets { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public virtual ICollection<WarehouseReceipt> WarehouseReceipts { get; set; } = new List<WarehouseReceipt>();

    public virtual ICollection<Lpn> Lpns { get; set; } = new List<Lpn>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
