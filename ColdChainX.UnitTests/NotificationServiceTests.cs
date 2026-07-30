using System.Security.Claims;
using ColdChainX.API.Controllers;
using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SecurityClaim = System.Security.Claims.Claim;

namespace ColdChainX.UnitTests;

public class NotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly FakeFirebaseMessagingClient _firebase = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new NotificationService(
            _db,
            _firebase,
            NullLogger<NotificationService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RegisterToken_NewToken_CreatesActiveDevice()
    {
        var user = await AddUserAsync("new-device");

        var result = await _service.RegisterDeviceTokenAsync(
            user.UserId,
            Registration("token-new", "Android", "device-1"));

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsActive);
        Assert.Equal("Android", result.Data.Platform);
        Assert.Single(await _db.DeviceTokens.ToListAsync());
        Assert.DoesNotContain("token-new", System.Text.Json.JsonSerializer.Serialize(result.Data));
    }

    [Fact]
    public async Task RegisterToken_SameTokenTwice_UpdatesOneRow()
    {
        var user = await AddUserAsync("same-device");

        await _service.RegisterDeviceTokenAsync(
            user.UserId,
            Registration("token-same", "Android", "device-1"));
        var result = await _service.RegisterDeviceTokenAsync(
            user.UserId,
            Registration("token-same", "iOS", "device-1", "iPhone"));

        Assert.True(result.Success);
        var token = Assert.Single(await _db.DeviceTokens.ToListAsync());
        Assert.Equal("iOS", token.Platform);
        Assert.Equal("iPhone", token.DeviceName);
        Assert.Equal(user.UserId, token.UserId);
    }

    [Fact]
    public async Task RegisterToken_AnotherUser_ReassignsExistingRow()
    {
        var firstUser = await AddUserAsync("first-owner");
        var secondUser = await AddUserAsync("second-owner");
        await _service.RegisterDeviceTokenAsync(
            firstUser.UserId,
            Registration("shared-token", "Android", "shared-device"));

        var result = await _service.RegisterDeviceTokenAsync(
            secondUser.UserId,
            Registration("shared-token", "Android", "shared-device"));

        Assert.True(result.Success);
        var token = Assert.Single(await _db.DeviceTokens.ToListAsync());
        Assert.Equal(secondUser.UserId, token.UserId);
        Assert.True(token.IsActive);
    }

    [Fact]
    public async Task UnregisterToken_OwnToken_DeactivatesIt()
    {
        var user = await AddUserAsync("unregister-owner");
        await _service.RegisterDeviceTokenAsync(
            user.UserId,
            Registration("owned-token", "Android", "owned-device"));

        var result = await _service.UnregisterDeviceTokenAsync(user.UserId, "owned-token");

        Assert.True(result.Success);
        Assert.False((await _db.DeviceTokens.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task UnregisterToken_AnotherUsersToken_IsIdempotentAndDoesNotChangeIt()
    {
        var owner = await AddUserAsync("real-owner");
        var other = await AddUserAsync("other-user");
        await _service.RegisterDeviceTokenAsync(
            owner.UserId,
            Registration("private-token", "Android", "owner-device"));

        var result = await _service.UnregisterDeviceTokenAsync(other.UserId, "private-token");

        Assert.True(result.Success);
        Assert.True((await _db.DeviceTokens.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task GetNotifications_ReturnsOnlyAuthenticatedUsersRows()
    {
        var currentUser = await AddUserAsync("history-current");
        var anotherUser = await AddUserAsync("history-other");
        _db.Notifications.AddRange(
            DirectNotification(currentUser.UserId, "ORDER_UPDATED"),
            DirectNotification(anotherUser.UserId, "ORDER_UPDATED"));
        await _db.SaveChangesAsync();

        var result = await _service.GetUserNotificationsAsync(
            currentUser.UserId,
            null,
            null,
            1,
            10);

        Assert.True(result.Success);
        var notification = Assert.Single(result.Data!.Data);
        Assert.Equal(currentUser.UserId, notification.UserId);
    }

    [Fact]
    public async Task MarkAsRead_DoesNotUpdateAnotherUsersNotification()
    {
        var owner = await AddUserAsync("notification-owner");
        var other = await AddUserAsync("notification-other");
        var notification = DirectNotification(owner.UserId, "INCIDENT_CREATED");
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();

        var result = await _service.MarkAsReadAsync(
            other.UserId,
            notification.NotiId);

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
        Assert.False((await _db.Notifications.SingleAsync()).IsRead);
    }

    [Fact]
    public async Task SendToUser_NoActiveDevices_SavesFailedHistory()
    {
        var user = await AddUserAsync("no-device");

        var result = await _service.SendToUserAsync(
            user.UserId,
            "Test title",
            "Test body",
            "TEST");

        Assert.Equal(0, result.TotalTokens);
        Assert.Equal(0, result.SuccessfulSends);
        var history = Assert.Single(await _db.Notifications.ToListAsync());
        Assert.Equal("FAILED", history.DeliveryStatus);
        Assert.Contains("No active device", history.FailureReason);
    }

    [Fact]
    public async Task SendToUser_InvalidFirebaseToken_DeactivatesToken()
    {
        var user = await AddUserAsync("invalid-token");
        await _service.RegisterDeviceTokenAsync(
            user.UserId,
            Registration("expired-token", "Android", "expired-device"));
        _firebase.ResultFactory = _ => new FirebaseTokenSendResult
        {
            ErrorCode = "Unregistered",
            IsInvalidToken = true
        };

        var result = await _service.SendToUserAsync(
            user.UserId,
            "Test title",
            "Test body",
            "TEST");

        Assert.Equal(1, result.FailedSends);
        Assert.False((await _db.DeviceTokens.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task SendToUser_TemporaryFirebaseFailure_KeepsTokenActive()
    {
        var user = await AddUserAsync("temporary-token");
        await _service.RegisterDeviceTokenAsync(
            user.UserId,
            Registration("temporary-token", "iOS", "temporary-device"));
        _firebase.ResultFactory = _ => new FirebaseTokenSendResult
        {
            ErrorCode = "Unavailable",
            IsTemporaryFailure = true
        };

        var result = await _service.SendToUserAsync(
            user.UserId,
            "Test title",
            "Test body",
            "TEST");

        Assert.Equal(1, result.FailedSends);
        Assert.True((await _db.DeviceTokens.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task NotificationEndpoint_MissingJwtClaim_ReturnsUnauthorized()
    {
        var controller = Controller(environmentName: "Production");

        var response = await controller.RegisterToken(
            Registration("controller-token", "Android", "controller-device"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(response);
    }

    [Fact]
    public async Task TestSendEndpoint_NonAdminOutsideDevelopment_ReturnsForbidden()
    {
        var caller = await AddUserAsync("non-admin");
        var controller = Controller(environmentName: "Production");
        controller.ControllerContext.HttpContext.User = Principal(caller.UserId);

        var response = await controller.SendTest(
            new NotificationTestRequest
            {
                UserId = caller.UserId,
                Title = "Firebase test",
                Body = "Test body",
                Type = "TEST"
            },
            CancellationToken.None);

        Assert.IsType<ForbidResult>(response);
    }

    private NotificationController Controller(string environmentName)
    {
        var controller = new NotificationController(
            _service,
            new FakeWebHostEnvironment { EnvironmentName = environmentName });
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private async Task<User> AddUserAsync(string suffix)
    {
        var user = new User
        {
            UserId = Guid.NewGuid(),
            Username = $"user-{suffix}",
            FullName = $"User {suffix}",
            Email = $"{suffix}@example.com",
            Status = "ACTIVE"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static RegisterDeviceTokenRequest Registration(
        string token,
        string platform,
        string deviceId,
        string? deviceName = null)
        => new()
        {
            DeviceToken = token,
            Platform = platform,
            DeviceId = deviceId,
            DeviceName = deviceName,
            AppVersion = "1.0.0"
        };

    private static Notification DirectNotification(Guid userId, string type)
        => new()
        {
            NotiId = Guid.NewGuid(),
            UserId = userId,
            Params = "{}",
            Title = "Title",
            Body = "Body",
            Type = type,
            DataJson = "{}",
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            DeliveryStatus = "SENT"
        };

    private static ClaimsPrincipal Principal(Guid userId, params string[] roles)
    {
        var claims = new List<SecurityClaim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        claims.AddRange(roles.Select(role => new SecurityClaim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class FakeFirebaseMessagingClient : IFirebaseMessagingClient
    {
        public Func<string, FirebaseTokenSendResult> ResultFactory { get; set; } =
            _ => new FirebaseTokenSendResult { Success = true };

        public bool IsConfigured => true;

        public string? ConfigurationError => null;

        public Task<IReadOnlyList<FirebaseTokenSendResult>> SendToTokensAsync(
            IReadOnlyList<string> tokens,
            string title,
            string body,
            IReadOnlyDictionary<string, string> data,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<FirebaseTokenSendResult> results =
                tokens.Select(ResultFactory).ToList();
            return Task.FromResult(results);
        }

        public Task<FirebaseTokenSendResult> SendToTopicAsync(
            string topic,
            string title,
            string body,
            IReadOnlyDictionary<string, string> data,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ResultFactory(topic));
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ColdChainX.UnitTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
