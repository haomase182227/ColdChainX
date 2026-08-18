using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ColdChainX.API.Authorization;
using ColdChainX.API.Controllers;
using ColdChainX.Application.DTOs.Authorization;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColdChainX.UnitTests;

public sealed class OrderCreatePermissionAuthorizationTests : IAsyncLifetime
{
    private static readonly Guid AllowedUserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid DeniedUserId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private OrderServiceStub _orderService = null!;
    private PermissionServiceStub _permissionService = null!;

    public async Task InitializeAsync()
    {
        _orderService = new OrderServiceStub();
        _permissionService = new PermissionServiceStub(AllowedUserId);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(OrderController).Assembly);
        builder.Services
            .AddAuthentication(OrderPermissionAuthenticationHandler.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, OrderPermissionAuthenticationHandler>(
                OrderPermissionAuthenticationHandler.AuthenticationScheme,
                _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton<IPermissionService>(_permissionService);
        builder.Services.AddSingleton<IOrderService>(_orderService);

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
    }

    [Fact]
    public async Task CreateOrder_Anonymous_Returns401()
    {
        var response = await _client.PostAsync("/api/orders", ValidMinimalForm());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _orderService.CreateCalls);
    }

    [Fact]
    public async Task CreateOrder_WithoutOrderCreatePermission_Returns403()
    {
        using var request = CreateRequest(DeniedUserId, customerIdClaim: Guid.NewGuid());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _orderService.CreateCalls);
        Assert.Equal(PermissionCodes.OrderCreate, _permissionService.LastPermissionCode);
    }

    [Fact]
    public async Task CreateOrder_WithPermission_UsesCustomerIdClaim()
    {
        var claimCustomerId = Guid.NewGuid();
        var submittedCustomerId = Guid.NewGuid();
        using var request = CreateRequest(AllowedUserId, claimCustomerId, submittedCustomerId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _orderService.CreateCalls);
        Assert.Equal(claimCustomerId, _orderService.LastCustomerId);
        Assert.Equal(PermissionCodes.OrderCreate, _permissionService.LastPermissionCode);
    }

    [Fact]
    public async Task CreateOrder_InternalUserWithPermission_UsesSubmittedCustomerId()
    {
        var submittedCustomerId = Guid.NewGuid();
        using var request = CreateRequest(AllowedUserId, submittedCustomerId: submittedCustomerId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _orderService.CreateCalls);
        Assert.Equal(submittedCustomerId, _orderService.LastCustomerId);
    }

    [Fact]
    public async Task CreateOrder_InternalUserWithoutCustomerId_Returns400()
    {
        using var request = CreateRequest(AllowedUserId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _orderService.CreateCalls);
    }

    private static HttpRequestMessage CreateRequest(
        Guid userId,
        Guid? customerIdClaim = null,
        Guid? submittedCustomerId = null)
    {
        var content = ValidMinimalForm();
        if (submittedCustomerId.HasValue)
            content.Add(new StringContent(submittedCustomerId.Value.ToString()), "Customer_ID");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = content };
        request.Headers.Add(OrderPermissionAuthenticationHandler.UserIdHeader, userId.ToString());
        if (customerIdClaim.HasValue)
        {
            request.Headers.Add(
                OrderPermissionAuthenticationHandler.CustomerIdHeader,
                customerIdClaim.Value.ToString());
        }

        return request;
    }

    private static MultipartFormDataContent ValidMinimalForm()
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Frozen goods"), "Item_Name");
        content.Add(new StringContent("MEAT_SEAFOOD"), "Category");
        content.Add(new StringContent("Pallet"), "Packaging_Type");
        content.Add(new StringContent("Test destination"), "Dest_Address_Text");
        return content;
    }

    private sealed class PermissionServiceStub : IPermissionService
    {
        private readonly Guid _allowedUserId;

        public PermissionServiceStub(Guid allowedUserId)
        {
            _allowedUserId = allowedUserId;
        }

        public string? LastPermissionCode { get; private set; }

        public Task<bool> HasPermissionAsync(
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            LastPermissionCode = permissionCode;
            return Task.FromResult(
                userId == _allowedUserId
                && permissionCode == PermissionCodes.OrderCreate);
        }

        public Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RolePermissionMatrixDto> GetRolePermissionMatrixAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReplaceRolePermissionsAsync(Guid roleId, IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<UserPermissionDto>> GetUserPermissionOverridesAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<UserPermissionDto> UpsertUserPermissionAsync(Guid userId, Guid permissionId, UpsertUserPermissionRequest request, Guid grantedBy, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RevokeUserPermissionAsync(Guid userId, Guid permissionId, Guid revokedBy, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class OrderServiceStub : IOrderService
    {
        public int CreateCalls { get; private set; }
        public Guid? LastCustomerId { get; private set; }

        public Task<ApiResponse<CreateOrderResponse>> CreateOrderAsync(CreateOrderRequest request, Guid customerId)
        {
            CreateCalls++;
            LastCustomerId = customerId;
            return Task.FromResult(ApiResponse<CreateOrderResponse>.SuccessResponse(
                new CreateOrderResponse
                {
                    OrderId = Guid.NewGuid(),
                    TrackingCode = "TEST",
                    ItemName = request.ItemName ?? string.Empty,
                    Category = request.Category ?? string.Empty,
                    PackingType = request.PackagingType ?? string.Empty,
                    TempCondition = request.TempCondition.ToString(),
                    Status = "PENDING"
                }));
        }

        public Task<ApiResponse<PagedResult<OrderResponse>>> GetOrdersAsync(int pageNumber, int pageSize, string? status = null, Guid? routeId = null, Guid? scheduleId = null)
            => throw new NotSupportedException();

        public Task<ApiResponse<PagedResult<OrderScheduleSummaryResponse>>> GetOrderScheduleSummaryAsync(DateOnly? fromDate, DateOnly? toDate, Guid? routeId, int pageNumber, int pageSize)
            => throw new NotSupportedException();

        public Task<ApiResponse<OrderResponse>> GetOrderByIdAsync(Guid orderId)
            => throw new NotSupportedException();

        public Task<ApiResponse<PagedResult<CustomerOrderSummaryResponse>>> GetOrdersByCustomerAsync(Guid customerId, int pageNumber, int pageSize, string? status = null)
            => throw new NotSupportedException();

        public Task<ApiResponse<CreateOrderResponse>> UpdateOrderAsync(Guid orderId, UpdateOrderRequest request, Guid customerId)
            => throw new NotSupportedException();

        public Task<ApiResponse<CreateOrderResponse>> AdminUpdateOrderAsync(Guid orderId, UpdateOrderRequest request, Guid salesUserId)
            => throw new NotSupportedException();

        public Task<ApiResponse<ReviewOrderResponse>> ReviewOrderAsync(Guid orderId, ReviewOrderRequest request, Guid salesUserId)
            => throw new NotSupportedException();

        public Task<ApiResponse<bool>> UploadPhysicalPodAsync(Guid orderId, string physicalPodImageUrl)
            => throw new NotSupportedException();

        public Task<ApiResponse<byte[]>> ExportDigitalArchiveAsync(Guid orderId)
            => throw new NotSupportedException();

        public Task<ApiResponse<IReadOnlyCollection<ColdChainX.Application.DTOs.Routes.WarehouseOptionDto>>> GetOriginWarehousesForOrderAsync(Guid orderId)
            => throw new NotSupportedException();

        public Task<ApiResponse<PublicTrackingResponseDto>> GetPublicTrackingAsync(string trackingCode)
            => throw new NotSupportedException();

        public Task<ApiResponse<object>> GetPublicTemperatureChartAsync(string trackingCode, int maxPoints = 200)
            => throw new NotSupportedException();
    }
}

internal sealed class OrderPermissionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticationScheme = "OrderPermissionTest";
    public const string UserIdHeader = "X-Test-User-Id";
    public const string CustomerIdHeader = "X-Test-Customer-Id";

    public OrderPermissionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId)
            || !Guid.TryParse(userId, out var parsedUserId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, parsedUserId.ToString())
        };

        if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerId)
            && Guid.TryParse(customerId, out var parsedCustomerId))
        {
            claims.Add(new Claim("CustomerId", parsedCustomerId.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme));
        var ticket = new AuthenticationTicket(principal, AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
