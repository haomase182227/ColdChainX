using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.DTOs.FinancialReports;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class FinancialReportService : IFinancialReportService
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<FinancialReportService> _logger;

        public FinancialReportService(IApplicationDbContext db, ILogger<FinancialReportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse<FinancialSummaryResponse>> GetFinancialSummaryAsync(DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
                {
                    throw new ValidationException("FromDate must be earlier than ToDate");
                }

                var start = fromDate ?? DateTime.UtcNow.AddDays(-30);
                var end = toDate ?? DateTime.UtcNow;

                var startOnly = DateOnly.FromDateTime(start);
                var endOnly = DateOnly.FromDateTime(end);

                var invoices = await _db.Invoices
                    .Where(i => i.IssuedDate >= startOnly && i.IssuedDate <= endOnly)
                    .ToListAsync();

                var totalRevenue = invoices.Sum(i => i.GrandTotal);
                var totalTaxVat = invoices.Sum(i => i.TaxAmount);
                var paidInvoicesCount = invoices.Count(i => i.Status == "PAID");
                var unpaidInvoicesCount = invoices.Count(i => i.Status != "PAID");

                var codTransactions = await _db.PaymentTransactions
                    .Where(t => t.TransactionType == "IN" && t.Status == "COMPLETED" && t.CreatedAt >= start && t.CreatedAt <= end)
                    .ToListAsync();
                var totalCodCollected = codTransactions.Sum(t => t.Amount);

                var claimTransactions = await _db.PaymentTransactions
                    .Where(t => t.TransactionType == "OUT" && t.ClaimId != null && t.Status == "COMPLETED" && t.CreatedAt >= start && t.CreatedAt <= end)
                    .ToListAsync();
                var totalClaimPayout = claimTransactions.Sum(t => t.Amount);

                var claimsCount = await _db.Claims
                    .CountAsync(c => c.CreatedAt >= start && c.CreatedAt <= end);

                var incidentExpenses = await _db.IncidentReports
                    .Where(i => (i.ExpenseStatus == "REIMBURSED" || i.ExpenseStatus == "APPROVED") && i.ReportedAt >= start && i.ReportedAt <= end)
                    .ToListAsync();
                var totalDriverReimbursement = incidentExpenses.Sum(i => i.ApprovedAmount ?? i.DriverPaidAmount);

                var netCashFlow = (totalRevenue + totalCodCollected) - (totalClaimPayout + totalDriverReimbursement);

                var response = new FinancialSummaryResponse
                {
                    FromDate = start,
                    ToDate = end,
                    TotalRevenue = totalRevenue,
                    TotalTaxVat = totalTaxVat,
                    TotalCodCollected = totalCodCollected,
                    TotalClaimPayout = totalClaimPayout,
                    TotalDriverReimbursement = totalDriverReimbursement,
                    NetOperatingCashFlow = netCashFlow,
                    TotalInvoicesCount = invoices.Count,
                    PaidInvoicesCount = paidInvoicesCount,
                    UnpaidInvoicesCount = unpaidInvoicesCount,
                    TotalClaimsCount = claimsCount,
                    TotalIncidentsCount = incidentExpenses.Count
                };

                return ApiResponse<FinancialSummaryResponse>.SuccessResponse(response, "Thống kê tóm tắt tài chính thành công.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tổng hợp báo cáo tài chính");
                return ApiResponse<FinancialSummaryResponse>.Failure($"Lỗi khi tổng hợp báo cáo tài chính: {ex.Message}");
            }
        }

        public async Task<byte[]> ExportVatInvoicesCsvAsync(DateTime? fromDate, DateTime? toDate, string? status)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                throw new ValidationException("FromDate must be earlier than ToDate");
            }

            var start = fromDate ?? DateTime.UtcNow.AddDays(-30);
            var end = toDate ?? DateTime.UtcNow;
            var startOnly = DateOnly.FromDateTime(start);
            var endOnly = DateOnly.FromDateTime(end);

            var query = _db.Invoices
                .Include(i => i.Customer)
                .Include(i => i.InvoiceLines)
                    .ThenInclude(il => il.Order)
                .Where(i => i.IssuedDate >= startOnly && i.IssuedDate <= endOnly)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i => i.Status == status);
            }

            var list = await query.OrderByDescending(i => i.IssuedDate).ToListAsync();

            var sb = new StringBuilder();
            sb.Append('\uFEFF');

            sb.AppendLine("STT,Mã Hóa Đơn,Số HĐ GTGT (VAT),Khách Hàng,Mã Số Thuế,Mã Vận Đơn (Tracking),Ngày Phát Hành,Tiền Trước Thuế (VND),Thuế Suất (%),Tiền Thuế VAT (VND),Tổng Tiền Thanh Toán (VND),Trạng Thái,Link PDF Hóa Đơn");

            int index = 1;

            foreach (var inv in list)
            {
                var trackingCode = inv.InvoiceLines.FirstOrDefault()?.Order?.TrackingCode ?? "N/A";
                var customerName = EscapeCsv(inv.Customer?.CompanyName ?? "Khách lẻ");
                var taxCode = EscapeCsv(inv.Customer?.TaxCode ?? "");
                var vatNo = EscapeCsv(inv.VatInvoiceNo ?? "Chưa cấp");
                var invoiceCode = EscapeCsv(inv.InvoiceCode);
                var pdfUrl = EscapeCsv(inv.PdfUrl ?? "");
                var statusText = inv.Status == "PAID" ? "Đã thanh toán" : "Chưa thanh toán";

                sb.AppendLine($"{index++},{invoiceCode},{vatNo},{customerName},{taxCode},{trackingCode},{inv.IssuedDate:dd/MM/yyyy},{inv.SubTotal:0.##},{inv.TaxRate ?? 8},{inv.TaxAmount:0.##},{inv.GrandTotal:0.##},{statusText},{pdfUrl}");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public async Task<byte[]> ExportCodSettlementCsvAsync(DateTime? fromDate, DateTime? toDate, Guid? driverId)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                throw new ValidationException("FromDate must be earlier than ToDate");
            }

            var start = fromDate ?? DateTime.UtcNow.AddDays(-30);
            var end = toDate ?? DateTime.UtcNow;

            var query = _db.MasterTrips
                .Include(t => t.TripDrivers)
                    .ThenInclude(td => td.Driver)
                        .ThenInclude(d => d.User)
                .Include(t => t.Vehicle)
                .Where(t => t.CreatedAt >= start && t.CreatedAt <= end)
                .AsQueryable();

            if (driverId.HasValue)
            {
                query = query.Where(t => t.TripDrivers.Any(td => td.DriverId == driverId.Value));
            }

            var trips = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

            var sb = new StringBuilder();
            sb.Append('\uFEFF');
            sb.AppendLine("STT,Mã Chuyến Xe,Tên Tài Xế Chính,Số Điện Thoại,Biển Số Xe,Nhiệt Độ Thiết Lập (°C),Trạng Thái Chuyến,Ngày Bắt Đầu,Ngày Hoàn Tất");

            int index = 1;
            foreach (var t in trips)
            {
                var primaryDriver = t.TripDrivers.FirstOrDefault(td => td.DriverRole == "PRIMARY")?.Driver ?? t.TripDrivers.FirstOrDefault()?.Driver;
                var driverName = EscapeCsv(primaryDriver?.FullName ?? primaryDriver?.User?.FullName ?? "Chưa gán");
                var driverPhone = EscapeCsv(primaryDriver?.PhoneNumber ?? primaryDriver?.User?.Phone ?? "");
                var plate = EscapeCsv(t.Vehicle?.TruckPlate ?? "N/A");

                sb.AppendLine($"{index++},{EscapeCsv(t.TripId.ToString())},{driverName},{driverPhone},{plate},{t.TargetTemperature:0.#},{t.Status},{t.StartedAt?.ToString("dd/MM/yyyy HH:mm") ?? "Chưa chạy"},{t.CompletedAt?.ToString("dd/MM/yyyy HH:mm") ?? "Chưa hoàn tất"}");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public async Task<byte[]> ExportClaimsExpensesCsvAsync(DateTime? fromDate, DateTime? toDate)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            {
                throw new ValidationException("FromDate must be earlier than ToDate");
            }

            var start = fromDate ?? DateTime.UtcNow.AddDays(-30);
            var end = toDate ?? DateTime.UtcNow;

            var claims = await _db.Claims
                .Include(c => c.Order)
                    .ThenInclude(o => o!.Customer)
                .Where(c => c.CreatedAt >= start && c.CreatedAt <= end)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var incidents = await _db.IncidentReports
                .Include(i => i.ReportedByNavigation)
                .Where(i => i.ReportedAt >= start && i.ReportedAt <= end)
                .OrderByDescending(i => i.ReportedAt)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.Append('\uFEFF');
            sb.AppendLine("STT,Mã Tham Chiếu,Phân Loại Chi Phí,Nội Dung Sự Cố / Lý Do,Người Thụ Hưởng,Trách Nhiệm / Trạng Thái,Ngày Phát Sinh");

            int index = 1;

            foreach (var c in claims)
            {
                var client = EscapeCsv(c.Order?.Customer?.CompanyName ?? "Khách hàng");
                var reason = EscapeCsv($"[Claim Mất Nhiệt/Hỏng Hàng] Vận đơn {c.Order?.TrackingCode ?? "N/A"}: {c.Description ?? "Hàng hỏng tại Dock"}");
                var fault = EscapeCsv($"Trách nhiệm: {c.FaultOwner ?? "Chưa xác định"} | Trạng thái: {c.Status}");

                sb.AppendLine($"{index++},{EscapeCsv(c.ClaimCode)},Chi Bồi Thường Claim Khách Hàng,{reason},{client},{fault},{c.CreatedAt?.ToString("dd/MM/yyyy HH:mm") ?? ""}");
            }

            foreach (var inc in incidents)
            {
                var driver = EscapeCsv(inc.ReportedByNavigation?.FullName ?? "Tài xế");
                var reason = EscapeCsv($"[Sự Cố Xe Dọc Đường] {inc.IncidentType}: {inc.Description ?? "Sửa chữa / Cứu hộ"}");
                var status = EscapeCsv($"Trạng thái hoàn phí: {inc.ExpenseStatus} | Đã chi: {inc.DriverPaidAmount:0.##} VND | Duyệt: {inc.ApprovedAmount ?? 0:0.##} VND");

                sb.AppendLine($"{index++},{EscapeCsv(inc.IncidentId.ToString())},Hoàn Chi Phí Sự Cố Cho Tài Xế,{reason},{driver},{status},{inc.ReportedAt?.ToString("dd/MM/yyyy HH:mm") ?? ""}");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }
            return text;
        }
    }
}
