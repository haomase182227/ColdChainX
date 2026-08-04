using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.FinancialReports;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IFinancialReportService
    {
        /// <summary>
        /// Lấy số liệu tổng hợp doanh thu, thuế GTGT, COD, chi bồi thường và dòng tiền thuần.
        /// </summary>
        Task<ApiResponse<FinancialSummaryResponse>> GetFinancialSummaryAsync(DateTime? fromDate, DateTime? toDate);

        /// <summary>
        /// Xuất bảng kê Hóa đơn VAT và doanh thu cước lạnh tức thì theo từng đơn hàng (File CSV UTF-8 mở Excel không lỗi font).
        /// </summary>
        Task<byte[]> ExportVatInvoicesCsvAsync(DateTime? fromDate, DateTime? toDate, string? status);

        /// <summary>
        /// Xuất bảng kê đối soát tiền mặt COD nộp về của Đội xe (File CSV UTF-8 mở Excel không lỗi font).
        /// </summary>
        Task<byte[]> ExportCodSettlementCsvAsync(DateTime? fromDate, DateTime? toDate, Guid? driverId);

        /// <summary>
        /// Xuất bảng quyết toán quỹ chi bồi thường Claim khách hàng và hoàn phí sự cố cho tài xế (File CSV UTF-8).
        /// </summary>
        Task<byte[]> ExportClaimsExpensesCsvAsync(DateTime? fromDate, DateTime? toDate);
    }
}
