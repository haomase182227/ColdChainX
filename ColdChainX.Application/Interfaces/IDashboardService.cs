using ColdChainX.Application.DTOs.Dashboards;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces;

public interface IDashboardService
{
    Task<ApiResponse<SalesOverviewResponse>> GetSalesOverviewAsync(
        DateTime? fromDate,
        DateTime? toDate,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<DispatcherOverviewResponse>> GetDispatcherOverviewAsync(
        DateOnly? date,
        Guid? warehouseId,
        string? scheduleRange = "DAY",
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AdminOverviewResponse>> GetAdminOverviewAsync(
        DateTime? fromDate,
        DateTime? toDate,
        Guid? warehouseId,
        Guid? routeId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AccountantOverviewResponse>> GetAccountantOverviewAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? groupBy,
        CancellationToken cancellationToken = default);
}
