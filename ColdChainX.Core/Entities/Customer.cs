using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Customer
{
    public Guid CustomerId { get; set; }

    public string CompanyName { get; set; } = null!;

    public string TaxCode { get; set; } = null!;

    public string? Address { get; set; }

    public string? Email { get; set; }

    public int? PaymentTerm { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }



    public virtual ICollection<CustomerContract> CustomerContracts { get; set; } = new List<CustomerContract>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();

    public virtual ICollection<TransportOrder> TransportOrders { get; set; } = new List<TransportOrder>();
}
