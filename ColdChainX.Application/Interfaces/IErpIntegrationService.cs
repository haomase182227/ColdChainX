using System;
using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Interfaces;

public interface IErpIntegrationService
{
    Task<ErpInventorySyncResponse> DeductInventoryAsync(Guid orderId, string itemCode, int quantity, CancellationToken cancellationToken = default);
    Task<ErpInvoiceResponse> GenerateVatInvoiceAsync(Guid orderId, decimal totalAmount, string customerName, CancellationToken cancellationToken = default);
}

public class ErpInventorySyncResponse
{
    public bool IsSuccess { get; set; }
    public string TransactionId { get; set; } = null!;
    public string ErpSystem { get; set; } = null!; // e.g. "MISA/SAP (Mock)"
    public string Message { get; set; } = null!;
    public DateTime SyncedAt { get; set; }
}

public class ErpInvoiceResponse
{
    public bool IsSuccess { get; set; }
    public string InvoiceNo { get; set; } = null!; // e.g. "VAT-2026-000123"
    public string LookupCode { get; set; } = null!;
    public string ElectronicInvoiceUrl { get; set; } = null!;
    public DateTime IssuedAt { get; set; }
}
