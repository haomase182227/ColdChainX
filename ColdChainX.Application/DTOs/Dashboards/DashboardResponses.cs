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
    public SalesKpis OverdueKpis { get; set; } = new();
    public IReadOnlyCollection<SalesFunnelItem> Funnel { get; set; } = Array.Empty<SalesFunnelItem>();
    public IReadOnlyCollection<StatusCountResponse> QuotationStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<QuotationValueByMonth> QuotationValuesByMonth { get; set; } = Array.Empty<QuotationValueByMonth>();
    public SalesAverageProcessingTimes AverageProcessingTimes { get; set; } = new();
    public IReadOnlyCollection<ReviewReasonCount> ReviewReasons { get; set; } = Array.Empty<ReviewReasonCount>();
    public IReadOnlyCollection<SalesPriorityWorkItem> PriorityWorkItems { get; set; } = Array.Empty<SalesPriorityWorkItem>();
    public IReadOnlyCollection<DashboardDistributionItem> WorkDistribution { get; set; } = Array.Empty<DashboardDistributionItem>();
    public IReadOnlyCollection<OrderVolumePeriod> OrderVolumeSeries { get; set; } = Array.Empty<OrderVolumePeriod>();
    public DiscrepancySummaryResponse DiscrepancySummary { get; set; } = new();
    public IReadOnlyCollection<DiscrepancyPeriod> DiscrepancySeries { get; set; } = Array.Empty<DiscrepancyPeriod>();
}

public class DashboardDistributionItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class OrderVolumePeriod
{
    public string Period { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
}

public class DiscrepancySummaryResponse
{
    public int TotalOrders { get; set; }
    public int DiscrepancyOrders { get; set; }
    public decimal DiscrepancyRate { get; set; }
}

public class DiscrepancyPeriod
{
    public string Period { get; set; } = string.Empty;
    public int Pending { get; set; }
    public int AppendixSent { get; set; }
    public int Resolved { get; set; }
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
    public IReadOnlyCollection<WarehouseResourceCount> ReadyLpnsByWarehouse { get; set; } = Array.Empty<WarehouseResourceCount>();
    public IReadOnlyCollection<WarehouseResourceCount> AvailableVehiclesByWarehouse { get; set; } = Array.Empty<WarehouseResourceCount>();
    public IReadOnlyCollection<StatusCountResponse> VehicleStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<WarehouseResourceCount> AvailableDriversByWarehouse { get; set; } = Array.Empty<WarehouseResourceCount>();
    public IReadOnlyCollection<StatusCountResponse> DriverStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<ScheduleReadinessItem> ScheduleReadiness { get; set; } = Array.Empty<ScheduleReadinessItem>();
}

public class WarehouseResourceCount
{
    public Guid? WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ScheduleReadinessItem
{
    public Guid ScheduleId { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public Guid RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public DateTime DepartureAt { get; set; }
    public int TotalOrders { get; set; }
    public int ReadyOrders { get; set; }
    public int NotReadyOrders { get; set; }
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
    public string Severity { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public Guid? TripId { get; set; }
    public string? TripCode { get; set; }
    public string? VehiclePlate { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string ActionType { get; set; } = string.Empty;
}

public class DashboardWorkItem
{
    public string Type { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Code { get; set; }
    public Guid? TripId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
    public DateTime? SlaDeadline { get; set; }
}

public class AdminOverviewResponse
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string GroupBy { get; set; } = "WEEK";
    public AdminOrderOverview OrderOverview { get; set; } = new();
    public AdminTripOverview TripOverview { get; set; } = new();
    public AdminFleetOverview FleetOverview { get; set; } = new();
    public AdminDriverOverview DriverOverview { get; set; } = new();
    public IReadOnlyCollection<RouteDemandItem> RouteDemand { get; set; } = Array.Empty<RouteDemandItem>();
    public IReadOnlyCollection<ServiceUsageItem> ServiceUsage { get; set; } = Array.Empty<ServiceUsageItem>();
    public IReadOnlyCollection<WarehouseResourceCount> LpnsByWarehouse { get; set; } = Array.Empty<WarehouseResourceCount>();
    public AdminIotOverview IotOverview { get; set; } = new();
    public AdminKpis Kpis { get; set; } = new();
    public IReadOnlyCollection<StatusCountResponse> VehicleStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<StatusCountResponse> IotStatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<TripPerformancePeriod> TripPerformanceByPeriod { get; set; } = Array.Empty<TripPerformancePeriod>();
    public IReadOnlyCollection<RouteTemperatureCompliance> TemperatureComplianceByRoute { get; set; } = Array.Empty<RouteTemperatureCompliance>();
    public IReadOnlyCollection<IncidentDistributionItem> IncidentDistribution { get; set; } = Array.Empty<IncidentDistributionItem>();
    public IReadOnlyCollection<TripsByWarehouseItem> TripsByWarehouse { get; set; } = Array.Empty<TripsByWarehouseItem>();
    public IReadOnlyCollection<FleetUtilizationItem> FleetUtilization { get; set; } = Array.Empty<FleetUtilizationItem>();
    public FinancialSnapshotResponse FinancialSnapshot { get; set; } = new();
    public IReadOnlyCollection<DashboardWorkItem> PriorityWorkItems { get; set; } = Array.Empty<DashboardWorkItem>();
}

public class AdminOrderOverview
{
    public int TotalOrders { get; set; }
    public IReadOnlyCollection<StatusCountResponse> StatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<StatusPeriodItem> ByPeriod { get; set; } = Array.Empty<StatusPeriodItem>();
}

public class StatusPeriodItem
{
    public string Period { get; set; } = string.Empty;
    public int Total { get; set; }
    public IReadOnlyCollection<StatusCountResponse> StatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
}

public class AdminTripOverview
{
    public int TotalTrips { get; set; }
    public int CompletedTrips { get; set; }
    public int SuccessfulTrips { get; set; }
    public int TripsWithIncidents { get; set; }
    public decimal IncidentRate { get; set; }
    public decimal DeliverySuccessRate { get; set; }
    public IReadOnlyCollection<StatusCountResponse> StatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<TripOperationPeriod> ByPeriod { get; set; } = Array.Empty<TripOperationPeriod>();
}

public class TripOperationPeriod
{
    public string Period { get; set; } = string.Empty;
    public int TotalTrips { get; set; }
    public int CompletedTrips { get; set; }
    public int SuccessfulTrips { get; set; }
    public int TripsWithIncidents { get; set; }
    public decimal IncidentRate { get; set; }
    public decimal DeliverySuccessRate { get; set; }
    public IReadOnlyCollection<StatusCountResponse> StatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
}

public class AdminFleetOverview
{
    public int TotalVehicles { get; set; }
    public int AvailableVehicles { get; set; }
    public IReadOnlyCollection<StatusCountResponse> StatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<WarehouseResourceCount> AvailableByWarehouse { get; set; } = Array.Empty<WarehouseResourceCount>();
    public IReadOnlyCollection<FleetUtilizationItem> TopUsedVehicles { get; set; } = Array.Empty<FleetUtilizationItem>();
}

public class AdminDriverOverview
{
    public int TotalDrivers { get; set; }
    public int AvailableDrivers { get; set; }
    public IReadOnlyCollection<StatusCountResponse> StatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
    public IReadOnlyCollection<WarehouseResourceCount> AvailableByWarehouse { get; set; } = Array.Empty<WarehouseResourceCount>();
    public IReadOnlyCollection<DriverUtilizationItem> TopUsedDrivers { get; set; } = Array.Empty<DriverUtilizationItem>();
}

public class DriverUtilizationItem
{
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public int TripCount { get; set; }
}

public class RouteDemandItem
{
    public Guid RouteId { get; set; }
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal Percentage { get; set; }
}

public class ServiceUsageItem
{
    public Guid? ServiceCatalogId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public int UsageCount { get; set; }
    public decimal Percentage { get; set; }
}

public class AdminIotOverview
{
    public int TotalDevices { get; set; }
    public IReadOnlyCollection<StatusCountResponse> StatusDistribution { get; set; } = Array.Empty<StatusCountResponse>();
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
    public int UnassignedIotDevices { get; set; }
    public int ExpiringDocuments { get; set; }
    public int ExpiredDocuments { get; set; }
    public int ExpiringVehicleDocuments { get; set; }
    public int ExpiredVehicleDocuments { get; set; }
    public int ExpiringDriverDocuments { get; set; }
    public int ExpiredDriverDocuments { get; set; }
    public int OpenIncidents { get; set; }
    public int OpenClaims { get; set; }
    public int OverdueClaims { get; set; }
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

public class IncidentDistributionItem
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TripsByWarehouseItem
{
    public Guid? WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int TripCount { get; set; }
    public int OrderCount { get; set; }
}

public class FleetUtilizationItem
{
    public Guid VehicleId { get; set; }
    public string VehiclePlate { get; set; } = string.Empty;
    public int TripCount { get; set; }
    public decimal UtilizationRate { get; set; }
}

public class AccountantOverviewResponse
{
    public AccountantKpis Kpis { get; set; } = new();
    public DateOnly ReceivablesAsOfDate { get; set; }
    public IReadOnlyCollection<CashFlowPeriod> CashFlowSeries { get; set; } = Array.Empty<CashFlowPeriod>();
    public IReadOnlyCollection<InvoiceStatusDistributionItem> InvoiceStatusDistribution { get; set; } = Array.Empty<InvoiceStatusDistributionItem>();
    public IReadOnlyCollection<ReceivablesAgingItem> ReceivablesAging { get; set; } = Array.Empty<ReceivablesAgingItem>();
    public IReadOnlyCollection<PaymentMethodSummary> CodByPaymentMethod { get; set; } = Array.Empty<PaymentMethodSummary>();
    public IReadOnlyCollection<ClaimPayoutTypeSummary> ClaimPayoutByType { get; set; } = Array.Empty<ClaimPayoutTypeSummary>();
    public IReadOnlyCollection<TopCustomerRevenueItem> TopCustomersByRevenue { get; set; } = Array.Empty<TopCustomerRevenueItem>();
    public IReadOnlyCollection<TopRouteRevenueItem> TopRoutesByRevenue { get; set; } = Array.Empty<TopRouteRevenueItem>();
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

public class TopCustomerRevenueItem
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class TopRouteRevenueItem
{
    public Guid RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class AccountantPriorityWorkItem
{
    public string Type { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsOverdue { get; set; }
}
