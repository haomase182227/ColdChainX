using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Invoice
{
    public Guid InvoiceId { get; set; }

    public string InvoiceCode { get; set; } = null!;

    public Guid CustomerId { get; set; }

    public string? VatInvoiceNo { get; set; }

    public string? PdfUrl { get; set; }

    public decimal SubTotal { get; set; }

    public decimal? TaxRate { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal? DeductionAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public decimal? PaidAmount { get; set; }

    public DateOnly IssuedDate { get; set; }

    public DateOnly DueDate { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();
}
