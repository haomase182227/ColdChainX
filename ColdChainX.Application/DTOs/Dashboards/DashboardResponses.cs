namespace ColdChainX.Application.DTOs.Dashboards;

public class StatusCountResponse
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class SalesOverviewResponse
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public SalesKpis Kpis { get; set; } = new();
    public IReadOnlyCollection<SalesFunnelItem> Funnel { get; set; } = Array.Empty<SalesFunnelItem>();
    public IReadOnlyCollection<StatusCountResponse> QuotationStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<QuotationValueByMonth> QuotationValuesByMonth { get; set; } = Array.Empty<QuotationValueByMonth>();
    public SalesAverageProcessingTimes AverageProcessingTimes { get; set; } = new();
    public IReadOnlyCollection<ReviewReasonCount> ReviewReasons { get; set; } = Array.Empty<ReviewReasonCount>();
    public IReadOnlyCollection<SalesPriorityWorkItem> PriorityWorkItems { get; set; } = Array.Empty<SalesPriorityWorkItem>();
}

public class SalesKpis
{
    public int PendingReviewOrders { get; set; }
    public int NeedsUpdateOrders { get; set; }
    public int DraftQuotations { get; set; }
    public int SentQuotations { get; set; }
    public int DraftContracts { get; set; }
    public int PendingCustomerSignature { get; set; }
    public int PendingSalesVerification { get; set; }
    public int UnreadMessages { get; set; }
}

public class SalesFunnelItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal ConversionRate { get; set; }
}

public class QuotationValueByMonth
{
    public string Month { get; set; } = string.Empty;
    public decimal SentValue { get; set; }
    public decimal AcceptedValue { get; set; }
}

public class SalesAverageProcessingTimes
{
    public decimal? OrderToQuotationSentHours { get; set; }
    public decimal? SignedUploadToVerificationHours { get; set; }
}

public class ReviewReasonCount
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class SalesPriorityWorkItem
{
    public string Type { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public Guid? OrderId { get; set; }
    public string? TrackingCode { get; set; }
    public string? CustomerName { get; set; }
    public decimal WaitingHours { get; set; }
    public bool IsOverdue { get; set; }
}

public class DispatcherOverviewResponse
{
    public DispatcherKpis Kpis { get; set; } = new();
    public IReadOnlyCollection<StatusCountResponse> TripStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<TripUtilizationItem> TripUtilization { get; set; } = Array.Empty<TripUtilizationItem>();
    public DeliveryPerformanceResponse DeliveryPerformance { get; set; } = new();
    public IReadOnlyCollection<DashboardAlertItem> PriorityAlerts { get; set; } = Array.Empty<DashboardAlertItem>();
    public IReadOnlyCollection<DashboardWorkItem> PriorityWorkItems { get; set; } = Array.Empty<DashboardWorkItem>();
}

public class DispatcherKpis
{
    public int ReadyLpns { get; set; }
    public int PlannedTrips { get; set; }
    public int PickingTrips { get; set; }
    public int ReadyToSealTrips { get; set; }
    public int InTransitTrips { get; set; }
    public int LateOrRiskTrips { get; set; }
    public int AvailableVehicles { get; set; }
    public int AvailableDrivers { get; set; }
    public int RedeliveryLpns { get; set; }
    public int PendingDispatcherClaims { get; set; }
}

public class TripUtilizationItem
{
    public Guid TripId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string? VehiclePlate { get; set; }
    public decimal WeightUtilizationPercent { get; set; }
    public decimal VolumeUtilizationPercent { get; set; }
}

public class DeliveryPerformanceResponse
{
    public int OnTimeTrips { get; set; }
    public int LateTrips { get; set; }
}

public class DashboardAlertItem
{
    public Guid AlertId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public Guid? TripId { get; set; }
    public string? TripCode { get; set; }
    public string? VehiclePlate { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

public class DashboardWorkItem
{
    public string Type { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AdminOverviewResponse
{
    public AdminKpis Kpis { get; set; } = new();
    public IReadOnlyCollection<StatusCountResponse> VehicleStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<StatusCountResponse> IotStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<TripPerformancePeriod> TripPerformanceByPeriod { get; set; } = Array.Empty<TripPerformancePeriod>();
    public IReadOnlyCollection<RouteTemperatureCompliance> TemperatureComplianceByRoute { get; set; } = Array.Empty<RouteTemperatureCompliance>();
    public FinancialSnapshotResponse FinancialSnapshot { get; set; } = new();
    public IReadOnlyCollection<DashboardWorkItem> PriorityWorkItems { get; set; } = Array.Empty<DashboardWorkItem>();
}

public class AdminKpis
{
    public int ActiveTrips { get; set; }
    public int LateTrips { get; set; }
    public int TripsWithTemperatureAlerts { get; set; }
    public int TotalVehicles { get; set; }
    public int VehiclesOnTrip { get; set; }
    public int VehiclesUnderMaintenance { get; set; }
    public int AvailableDrivers { get; set; }
    public int DriversOnTrip { get; set; }
    public int DriversRelaxing { get; set; }
    public int OnlineIotDevices { get; set; }
    public int OfflineIotDevices { get; set; }
    public int ExpiringDocuments { get; set; }
    public int ExpiredDocuments { get; set; }
    public int OpenIncidents { get; set; }
    public int OpenClaims { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }
}

public class TripPerformancePeriod
{
    public string Period { get; set; } = string.Empty;
    public int Completed { get; set; }
    public int Late { get; set; }
    public int Incident { get; set; }
}

public class RouteTemperatureCompliance
{
    public Guid RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public decimal ComplianceRate { get; set; }
}

public class FinancialSnapshotResponse
{
    public decimal RecognizedRevenue { get; set; }
    public decimal NetCashFlow { get; set; }
    public decimal ClaimPayout { get; set; }
    public decimal UnpaidInvoiceAmount { get; set; }
}

public class AccountantOverviewResponse
{
    public AccountantKpis Kpis { get; set; } = new();
    public IReadOnlyCollection<CashFlowPeriod> CashFlowSeries { get; set; } = Array.Empty<CashFlowPeriod>();
    public IReadOnlyCollection<InvoiceStatusDistributionItem> InvoiceStatusDistribution { get; set; } = Array.Empty<InvoiceStatusDistributionItem>();
    public IReadOnlyCollection<ReceivablesAgingItem> ReceivablesAging { get; set; } = Array.Empty<ReceivablesAgingItem>();
    public IReadOnlyCollection<PaymentMethodSummary> CodByPaymentMethod { get; set; } = Array.Empty<PaymentMethodSummary>();
    public IReadOnlyCollection<ClaimPayoutTypeSummary> ClaimPayoutByType { get; set; } = Array.Empty<ClaimPayoutTypeSummary>();
    public IReadOnlyCollection<AccountantPriorityWorkItem> PriorityWorkItems { get; set; } = Array.Empty<AccountantPriorityWorkItem>();
}

public class AccountantKpis
{
    public decimal RecognizedRevenue { get; set; }
    public decimal CashCollected { get; set; }
    public decimal CodCollected { get; set; }
    public decimal Receivables { get; set; }
    public decimal VatAmount { get; set; }
    public decimal ClaimPayout { get; set; }
    public decimal DriverReimbursement { get; set; }
    public decimal NetCashFlow { get; set; }
    public int PendingAccountantClaims { get; set; }
    public int PendingVerificationTransactions { get; set; }
}

public class CashFlowPeriod
{
    public string Period { get; set; } = string.Empty;
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
}

public class InvoiceStatusDistributionItem
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public class ReceivablesAgingItem
{
    public string Bucket { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal Amount { get; set; }
}

public class PaymentMethodSummary
{
    public string PaymentMethod { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public class ClaimPayoutTypeSummary
{
    public string ClaimType { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public class AccountantPriorityWorkItem
{
    public string Type { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTime? CreatedAt { get; set; }
}
