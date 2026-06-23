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
        Task<string> SaveContractAppendixPdfAsync(string htmlContent, string appendixNumber);
        Task<string> SaveInboundReturnSlipPdfAsync(string htmlContent, string slipCode);

        /// <summary>Sinh Manifest PDF (biên bản hàng ghép / LIFO diagram) theo tripId và upload lên Cloudinary.</summary>
        Task<string> GenerateManifestPdfAsync(Guid tripId);

        /// <summary>Sinh Phiếu Xuất Kho PDF theo tripId và upload lên Cloudinary.</summary>
        Task<string> GenerateOutboundTicketPdfAsync(Guid tripId);
    }
}
