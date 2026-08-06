using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using ColdChainX.API.Controllers;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Dashboards;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColdChainX.UnitTests;

public sealed class AuthorizationIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private SqliteTestDatabase _database = null!;
    private IncidentServiceStub _incidentStub = null!;

    public async Task InitializeAsync()
    {
        _database = new SqliteTestDatabase();
        _incidentStub = new IncidentServiceStub();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(IncidentReportsController).Assembly);
        builder.Services.AddAuthentication(TestAuthenticationHandler.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.AuthenticationScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IIncidentReportService>(_incidentStub);
        builder.Services.AddSingleton<IIncidentRescueService, IncidentRescueServiceStub>();
        builder.Services.AddSingleton<IDashboardService, DashboardServiceStub>();
        builder.Services.AddSingleton<IContractService>(
            new ContractService(_database.Db, null!, null!, null!, null!));

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _database.Dispose();
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reimburse")]
    public async Task IncidentExpenseEndpoints_Anonymous_Return401(string action)
    {
        var response = await SendExpenseAsync(action, role: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reimburse")]
    public async Task IncidentExpenseEndpoints_AuthenticatedUnauthorizedRole_Return403(string action)
    {
        var response = await SendExpenseAsync(action, "Driver");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("approve", "Accountant")]
    [InlineData("approve", "ACCOUNTANT")]
    [InlineData("approve", "Admin")]
    [InlineData("approve", "ADMIN")]
    [InlineData("reimburse", "Accountant")]
    [InlineData("reimburse", "ACCOUNTANT")]
    [InlineData("reimburse", "Admin")]
    [InlineData("reimburse", "ADMIN")]
    public async Task IncidentExpenseEndpoints_AccountantAndPreviousAdminRoles_ReachBusinessValidation(
        string action,
        string role)
    {
        var callsBefore = _incidentStub.ExpenseCalls;

        var response = await SendExpenseAsync(action, role);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(callsBefore + 1, _incidentStub.ExpenseCalls);
        Assert.Contains("business validation", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Accountant_DoesNotGainAccessToUnrelatedResolveIncidentOperation()
    {
        using var request = WithRole(
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{Guid.NewGuid()}/resolve")
            {
                Content = Json("{\"resolutionNote\":\"done\"}")
            },
            "Accountant");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/dashboards/sales/overview")]
    [InlineData("/api/v1/dashboards/dispatcher/overview")]
    [InlineData("/api/v1/dashboards/admin/overview")]
    [InlineData("/api/v1/dashboards/accountant/overview")]
    [InlineData("/api/contracts")]
    public async Task ProtectedNewEndpoints_Anonymous_Return401(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/dashboards/sales/overview", "Sales")]
    [InlineData("/api/v1/dashboards/sales/overview", "Admin")]
    [InlineData("/api/v1/dashboards/dispatcher/overview", "Dispatcher")]
    [InlineData("/api/v1/dashboards/dispatcher/overview", "Admin")]
    [InlineData("/api/v1/dashboards/admin/overview", "Admin")]
    [InlineData("/api/v1/dashboards/accountant/overview", "Accountant")]
    [InlineData("/api/v1/dashboards/accountant/overview", "Admin")]
    [InlineData("/api/contracts", "Sales")]
    [InlineData("/api/contracts", "Admin")]
    [InlineData("/api/contracts", "Dispatcher")]
    public async Task ProtectedNewEndpoints_ExpectedRoles_Return200(string path, string role)
    {
        using var request = WithRole(new HttpRequestMessage(HttpMethod.Get, path), role);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/dashboards/sales/overview", "Accountant")]
    [InlineData("/api/v1/dashboards/dispatcher/overview", "Sales")]
    [InlineData("/api/v1/dashboards/admin/overview", "Dispatcher")]
    [InlineData("/api/v1/dashboards/accountant/overview", "Sales")]
    [InlineData("/api/contracts", "Accountant")]
    public async Task ProtectedNewEndpoints_WrongRole_Return403(string path, string role)
    {
        using var request = WithRole(new HttpRequestMessage(HttpMethod.Get, path), role);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendExpenseAsync(string action, string? role)
    {
        HttpRequestMessage request;
        if (action == "approve")
        {
            request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/incidents/{Guid.NewGuid()}/expenses/approve")
            {
                Content = Json("{\"approvedAmount\":100,\"approvalNote\":\"verified\"}")
            };
        }
        else
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent("100"), "ReimbursedAmount");
            content.Add(new StringContent("verified"), "Note");
            var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "ReceiptFile", "receipt.png");
            request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/incidents/{Guid.NewGuid()}/expenses/reimburse")
            {
                Content = content
            };
        }

        using (request)
        {
            if (role != null)
                request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
            return await _client.SendAsync(request);
        }
    }

    private static StringContent Json(string value)
        => new(value, Encoding.UTF8, "application/json");

    private static HttpRequestMessage WithRole(HttpRequestMessage request, string role)
    {
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
        return request;
    }

    private sealed class IncidentServiceStub : IIncidentReportService
    {
        public int ExpenseCalls { get; private set; }

        public Task<ApiResponse<IncidentResponse>> ApproveExpenseAsync(Guid incidentId, ApproveIncidentExpenseRequest request, Guid adminId)
        {
            ExpenseCalls++;
            return Task.FromResult(ApiResponse<IncidentResponse>.Failure("business validation preserved", 409));
        }

        public Task<ApiResponse<IncidentResponse>> ReimburseExpenseAsync(Guid incidentId, ReimburseIncidentExpenseRequest request, Guid adminId)
        {
            ExpenseCalls++;
            return Task.FromResult(ApiResponse<IncidentResponse>.Failure("business validation preserved", 409));
        }

        public Task<ApiResponse<IncidentResponse>> ReportIncidentAsync(CreateIncidentRequest request, Guid userId) => throw new NotSupportedException();
        public Task<ApiResponse<IncidentResponse>> AddEvidenceAsync(Guid incidentId, IReadOnlyCollection<Microsoft.AspNetCore.Http.IFormFile> files, string evidenceType, Guid userId) => throw new NotSupportedException();
        public Task<ApiResponse<bool>> ResolveIncidentAsync(Guid incidentId, ResolveIncidentRequest request, Guid userId) => throw new NotSupportedException();
        public Task<ApiResponse<IncidentResponse>> GetIncidentByIdAsync(Guid incidentId) => throw new NotSupportedException();
        public Task<ApiResponse<PagedResult<IncidentResponse>>> GetPagedIncidentsAsync(Guid? tripId, int pageNumber, int pageSize) => throw new NotSupportedException();
    }

    private sealed class IncidentRescueServiceStub : IIncidentRescueService
    {
        public Task<ApiResponse<List<RescueCandidateResponse>>> GetRescueCandidatesAsync(Guid incidentId) => throw new NotSupportedException();
        public Task<ApiResponse<IncidentWorkflowResult>> ContinueTripAsync(Guid incidentId, ContinueTripAfterIncidentRequest request, Guid driverUserId) => throw new NotSupportedException();
        public Task<ApiResponse<IncidentRescueResult>> DispatchRescueAsync(Guid incidentId, DispatchRescueRequest request, Guid dispatcherId) => throw new NotSupportedException();
        public Task<ApiResponse<IncidentWorkflowResult>> ConfirmTransloadAsync(Guid incidentId, ConfirmTransloadRequest request, Guid confirmedBy) => throw new NotSupportedException();
    }

    private sealed class DashboardServiceStub : IDashboardService
    {
        public Task<ApiResponse<SalesOverviewResponse>> GetSalesOverviewAsync(DateTime? fromDate, DateTime? toDate, Guid? userId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SalesOverviewResponse>.SuccessResponse(new SalesOverviewResponse()));

        public Task<ApiResponse<DispatcherOverviewResponse>> GetDispatcherOverviewAsync(DateOnly? date, Guid? warehouseId, string? scheduleRange = "DAY", CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<DispatcherOverviewResponse>.SuccessResponse(new DispatcherOverviewResponse()));

        public Task<ApiResponse<AdminOverviewResponse>> GetAdminOverviewAsync(DateTime? fromDate, DateTime? toDate, Guid? warehouseId, Guid? routeId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<AdminOverviewResponse>.SuccessResponse(new AdminOverviewResponse()));

        public Task<ApiResponse<AccountantOverviewResponse>> GetAccountantOverviewAsync(DateTime? fromDate, DateTime? toDate, string? groupBy, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<AccountantOverviewResponse>.SuccessResponse(new AccountantOverviewResponse()));
    }
}

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticationScheme = "Test";
    public const string RoleHeader = "X-Test-Role";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role) || string.IsNullOrWhiteSpace(role))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationScheme)));
    }
}
