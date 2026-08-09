using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.FinancialReports;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IFinancialReportService
    {
        Task<ApiResponse<FinancialSummaryResponse>> GetFinancialSummaryAsync(DateTime? fromDate, DateTime? toDate);

        Task<byte[]> ExportVatInvoicesCsvAsync(DateTime? fromDate, DateTime? toDate, string? status);

        Task<byte[]> ExportCodSettlementCsvAsync(DateTime? fromDate, DateTime? toDate, Guid? driverId);

        Task<byte[]> ExportClaimsExpensesCsvAsync(DateTime? fromDate, DateTime? toDate);
    }
}
