namespace ColdChainX.Application.Interfaces
{
    public interface IPdfService
    {
        Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber);
        Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber);
        Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode);
        Task<string> SaveWaybillPdfAsync(string htmlContent, string tripId);

        Task<string> SaveLifoMapPdfAsync(string htmlContent, string tripId);
        Task<string> SavePdfFromUrlAsync(string url, string fileId, string prefix);

        Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId);
        Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode);
        Task<string> SaveContractAppendixPdfAsync(string htmlContent, string appendixNumber);
        Task<string> SaveInboundReturnSlipPdfAsync(string htmlContent, string slipCode);

        Task<string> GenerateManifestPdfAsync(Guid tripId);

        Task<string> GenerateOutboundTicketPdfAsync(Guid tripId);
    }
}
