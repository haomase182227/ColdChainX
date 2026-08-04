using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.FinancialReports;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Financial and Tax Reporting Hub for Accountants (Báo cáo Tài chính, Bảng kê Thuế VAT, Đối soát COD và Quyết toán Bồi thường).
    /// </summary>
    [ApiController]
    [Route("api/v1/financial-reports")]
    [Authorize(Roles = "Accountant,ACCOUNTANT,Admin,ADMIN,Manager,MANAGER")]
    public class FinancialReportsController : ControllerBase
    {
        private readonly IFinancialReportService _reportService;

        public FinancialReportsController(IFinancialReportService reportService)
        {
            _reportService = reportService;
        }

        /// <summary>
        /// Lấy tóm tắt chỉ số tài chính (Doanh thu tức thì, Thuế GTGT, COD đã thu, Quỹ bồi thường Claim, Dòng tiền thuần).
        /// </summary>
        /// <param name="fromDate">Từ ngày (mặc định 30 ngày gần nhất)</param>
        /// <param name="toDate">Đến ngày</param>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(ApiResponse<FinancialSummaryResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSummary([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            var result = await _reportService.GetFinancialSummaryAsync(fromDate, toDate);
            return Ok(result);
        }

        /// <summary>
        /// Xuất file Excel/CSV Bảng kê Hóa đơn VAT và Doanh thu phát hành tức thì theo từng đơn hàng.
        /// </summary>
        /// <param name="fromDate">Từ ngày</param>
        /// <param name="toDate">Đến ngày</param>
        /// <param name="status">Trạng thái (ALL, PAID, UNPAID)</param>
        [HttpGet("vat-invoices/export")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ExportVatInvoices([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] string? status)
        {
            var bytes = await _reportService.ExportVatInvoicesCsvAsync(fromDate, toDate, status);
            var filename = $"Bang_Ke_Hoa_Don_VAT_Doanh_Thu_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
        }

        /// <summary>
        /// Xuất file Excel/CSV Bảng kê đối soát tiền mặt & COD thu hộ của Đội xe theo chuyến.
        /// </summary>
        /// <param name="fromDate">Từ ngày</param>
        /// <param name="toDate">Đến ngày</param>
        /// <param name="driverId">Lọc theo tài xế cụ thể (nếu có)</param>
        [HttpGet("cod-settlement/export")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ExportCodSettlement([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] Guid? driverId)
        {
            var bytes = await _reportService.ExportCodSettlementCsvAsync(fromDate, toDate, driverId);
            var filename = $"Bang_Ke_Doi_Soat_COD_Doi_Xe_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
        }

        /// <summary>
        /// Xuất file Excel/CSV Bảng quyết toán Quỹ chi bồi thường Claim hư hỏng hàng & Hoàn chi phí sự cố tài xế.
        /// </summary>
        /// <param name="fromDate">Từ ngày</param>
        /// <param name="toDate">Đến ngày</param>
        [HttpGet("claims-expenses/export")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ExportClaimsExpenses([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            var bytes = await _reportService.ExportClaimsExpensesCsvAsync(fromDate, toDate);
            var filename = $"Bang_Ke_Quyet_Toan_Boi_Thuong_Su_Co_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
        }
    }
}
