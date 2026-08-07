using System.Security.Claims;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/v1/dashboards")]
public class DashboardsController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardsController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("sales/overview")]
    [Authorize(Roles = "Sales,SALES,Admin,ADMIN")]
    public async Task<IActionResult> GetSalesOverview(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        Guid? userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
            ? parsedUserId
            : null;
        var result = await _dashboardService.GetSalesOverviewAsync(fromDate, toDate, userId, cancellationToken);
        return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
    }

    [HttpGet("dispatcher/overview")]
    [Authorize(Roles = "Dispatcher,DISPATCHER,Admin,ADMIN")]
    public async Task<IActionResult> GetDispatcherOverview(
        [FromQuery] DateOnly? date = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] string? scheduleRange = "DAY",
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetDispatcherOverviewAsync(date, warehouseId, scheduleRange, cancellationToken);
        return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
    }

    [HttpGet("admin/overview")]
    [Authorize(Roles = "Admin,ADMIN")]
    public async Task<IActionResult> GetAdminOverview(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] Guid? routeId = null,
        [FromQuery] string? groupBy = "WEEK",
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetAdminOverviewAsync(
            fromDate,
            toDate,
            warehouseId,
            routeId,
            groupBy,
            top,
            cancellationToken);
        return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
    }

    [HttpGet("accountant/overview")]
    [Authorize(Roles = "Accountant,ACCOUNTANT,Admin,ADMIN")]
    public async Task<IActionResult> GetAccountantOverview(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? groupBy = "DAY",
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetAccountantOverviewAsync(fromDate, toDate, groupBy, cancellationToken);
        return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
    }
}
