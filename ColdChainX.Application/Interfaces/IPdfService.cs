namespace ColdChainX.Application.Interfaces
{
    public interface IPdfService
    {
        Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber);
        Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber);
        Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode);
        Task<string> SaveWaybillPdfAsync(string htmlContent, string tripId);
        Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId);
        Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode);
    }
}
