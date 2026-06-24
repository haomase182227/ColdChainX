using System;

namespace ColdChainX.Application.DTOs.Invoices
{
    /// <summary>
    /// Response model for a single invoice line item.
    /// </summary>
    public class InvoiceLineResponse
    {
        public Guid LineId { get; set; }

        public Guid InvoiceId { get; set; }

        public Guid OrderId { get; set; }

        public string ChargeType { get; set; } = null!;

        public string Description { get; set; } = null!;

        public decimal? Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal Amount { get; set; }

        public decimal? TaxRate { get; set; }
    }
}
