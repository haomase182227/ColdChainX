namespace ColdChainX.Application.Interfaces
{
    public interface IPdfService
    {
        Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber);
        Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber);
        Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode);
        Task<string> SaveWaybillPdfAsync(string htmlContent, string tripId);

        /// <summary>LÆ°u PDF SÆ¡ Ä‘á»“ gá»™p chuyáº¿n / LIFO (prefix "lifo_") â€” KHÃ”NG trÃ¹ng tÃªn vá»›i giáº¥y Ä‘i Ä‘Æ°á»ng.</summary>
        Task<string> SaveLifoMapPdfAsync(string htmlContent, string tripId);
        Task<string> SavePdfFromUrlAsync(string url, string fileId, string prefix);

        Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId);
        Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode);
        Task<string> SaveContractAppendixPdfAsync(string htmlContent, string appendixNumber);
        Task<string> SaveInboundReturnSlipPdfAsync(string htmlContent, string slipCode);

        /// <summary>Sinh Manifest PDF (biÃªn báº£n hÃ ng ghÃ©p / LIFO diagram) theo tripId vÃ  upload lÃªn Cloudinary.</summary>
        Task<string> GenerateManifestPdfAsync(Guid tripId);

        /// <summary>Sinh Phiáº¿u Xuáº¥t Kho PDF theo tripId vÃ  upload lÃªn Cloudinary.</summary>
        Task<string> GenerateOutboundTicketPdfAsync(Guid tripId);
    }
}
