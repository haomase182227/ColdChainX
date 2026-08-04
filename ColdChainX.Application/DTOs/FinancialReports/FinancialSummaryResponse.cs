using System;

namespace ColdChainX.Application.DTOs.FinancialReports
{
    public class FinancialSummaryResponse
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public decimal TotalRevenue { get; set; }
        public decimal TotalTaxVat { get; set; }
        public decimal TotalCodCollected { get; set; }
        public decimal TotalClaimPayout { get; set; }
        public decimal TotalDriverReimbursement { get; set; }
        public decimal NetOperatingCashFlow { get; set; }

        public int TotalInvoicesCount { get; set; }
        public int PaidInvoicesCount { get; set; }
        public int UnpaidInvoicesCount { get; set; }
        public int TotalClaimsCount { get; set; }
        public int TotalIncidentsCount { get; set; }
    }

    public class VatInvoiceExportDto
    {
        public string InvoiceCode { get; set; } = string.Empty;
        public string VatInvoiceNo { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string OrderCode { get; set; } = string.Empty;
        public DateOnly IssuedDate { get; set; }
        public decimal SubTotal { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal GrandTotal { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? PdfUrl { get; set; }
    }

    public class CodSettlementExportDto
    {
        public string TripCode { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string DriverPhone { get; set; } = string.Empty;
        public string VehiclePlate { get; set; } = string.Empty;
        public decimal TotalCodAmount { get; set; }
        public decimal ActualCodCollected { get; set; }
        public string TripStatus { get; set; } = string.Empty;
        public string ReconciliationStatus { get; set; } = string.Empty;
        public DateTime? CompletedDate { get; set; }
    }

    public class ClaimsExpenseExportDto
    {
        public string ReferenceCode { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // "CLAIM_PAYOUT" hoặc "DRIVER_EXPENSE_REIMBURSE"
        public string TitleOrReason { get; set; } = string.Empty;
        public string BeneficiaryName { get; set; } = string.Empty; // Tên Khách Hàng hoặc Tên Tài Xế
        public decimal Amount { get; set; }
        public string FaultOwnerOrStatus { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
    }
}
