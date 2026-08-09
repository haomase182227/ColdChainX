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
    [ApiController]
    [Route("api/v1/financial-reports")]
[Authorize(Roles = "Accountant,Admin")]
    public class FinancialReportsController : ControllerBase
    {
        private readonly IFinancialReportService _reportService;

        public FinancialReportsController(IFinancialReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int? year = null,
            [FromQuery] int? month = null)
        {
            if (year.HasValue && (year.Value <= 0 || year.Value < 1900 || year.Value > 2100))
            {
                return BadRequest(ApiResponse<FinancialSummaryResponse>.Failure("Invalid year parameter (Year must be a valid positive integer)."));
            }

            if (month.HasValue && (month.Value < 1 || month.Value > 12))
            {
                return BadRequest(ApiResponse<FinancialSummaryResponse>.Failure("Invalid month parameter (Month must be between 1 and 12)."));
            }

            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                return BadRequest(ApiResponse<FinancialSummaryResponse>.Failure("FromDate must be earlier than ToDate"));
            }

            var result = await _reportService.GetFinancialSummaryAsync(fromDate, toDate);
            return Ok(result);
        }

        [HttpGet("vat-invoices/export")]
        public async Task<IActionResult> ExportVatInvoices([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] string? status)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                return BadRequest(ApiResponse<object>.Failure("FromDate must be earlier than ToDate"));
            }

            var bytes = await _reportService.ExportVatInvoicesCsvAsync(fromDate, toDate, status);
            var filename = $"Bang_Ke_Hoa_Don_VAT_Doanh_Thu_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
        }

        [HttpGet("cod-settlement/export")]
        public async Task<IActionResult> ExportCodSettlement([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] Guid? driverId)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                return BadRequest(ApiResponse<object>.Failure("FromDate must be earlier than ToDate"));
            }

            var bytes = await _reportService.ExportCodSettlementCsvAsync(fromDate, toDate, driverId);
            var filename = $"Bang_Ke_Doi_Soat_COD_Doi_Xe_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
        }

        [HttpGet("claims-expenses/export")]
        public async Task<IActionResult> ExportClaimsExpenses([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                return BadRequest(ApiResponse<object>.Failure("FromDate must be earlier than ToDate"));
            }

            var bytes = await _reportService.ExportClaimsExpensesCsvAsync(fromDate, toDate);
            var filename = $"Bang_Ke_Quyet_Toan_Boi_Thuong_Su_Co_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
        }
    }
}
