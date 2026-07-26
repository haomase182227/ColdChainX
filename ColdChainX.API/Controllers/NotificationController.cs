using System.Security.Claims;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IWebHostEnvironment _environment;

    public NotificationController(
        INotificationService notificationService,
        IWebHostEnvironment environment)
    {
        _notificationService = notificationService;
        _environment = environment;
    }

    /// <summary>Register or refresh the current user's mobile FCM token.</summary>
    [HttpPost("register-token")]
    [ProducesResponseType(
        typeof(ApiResponse<DeviceTokenRegistrationResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterToken(
        [FromBody] RegisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return InvalidToken();

        var result = await _notificationService.RegisterDeviceTokenAsync(
            userId,
            request,
            cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Deactivate an FCM token owned by the current user.</summary>
    [HttpDelete("unregister-token")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnregisterToken(
        [FromBody] UnregisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return InvalidToken();

        var result = await _notificationService.UnregisterDeviceTokenAsync(
            userId,
            request?.DeviceToken ?? string.Empty,
            cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Get the current user's notification history, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResult<NotificationResponse>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] bool? isRead = null,
        [FromQuery] string? type = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
            return InvalidToken();

        var result = await _notificationService.GetUserNotificationsAsync(
            userId,
            isRead,
            type,
            pageNumber,
            pageSize,
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Get one notification owned by the current user.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(
        typeof(ApiResponse<NotificationResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotification(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return InvalidToken();

        var result = await _notificationService.GetNotificationByIdAsync(
            userId,
            id,
            cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Get the current user's unread notification count.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(
        typeof(ApiResponse<UnreadCountResponse>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return InvalidToken();

        var result = await _notificationService.GetUnreadCountAsync(userId, cancellationToken);
        var response = result.Success
            ? ApiResponse<UnreadCountResponse>.SuccessResponse(
                new UnreadCountResponse { UnreadCount = result.Data },
                result.Message,
                result.StatusCode)
            : ApiResponse<UnreadCountResponse>.Failure(
                result.Message,
                result.StatusCode,
                result.Errors);

        return StatusCode(response.StatusCode, response);
    }

    /// <summary>Mark one notification owned by the current user as read.</summary>
    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return InvalidToken();

        var result = await _notificationService.MarkAsReadAsync(
            userId,
            id,
            cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Mark all notifications owned by the current user as read.</summary>
    [HttpPut("read-all")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return InvalidToken();

        var result = await _notificationService.MarkAllAsReadAsync(
            userId,
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Send a test notification. Any authenticated user may use this in Development;
    /// outside Development it is restricted to the Admin role.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(
        typeof(ApiResponse<NotificationTestResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SendTest(
        [FromBody] NotificationTestRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _))
            return InvalidToken();

        if (!_environment.IsDevelopment() &&
            !User.IsInRole("Admin") &&
            !User.IsInRole("ADMIN"))
        {
            return Forbid();
        }

        if (request == null ||
            request.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.Body) ||
            string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest(ApiResponse<NotificationTestResponse>.Failure(
                "UserId, title, body, and type are required."));
        }

        var sendResult = await _notificationService.SendToUserAsync(
            request.UserId,
            request.Title,
            request.Body,
            request.Type,
            request.ReferenceId,
            new Dictionary<string, string>
            {
                ["screen"] = "notifications"
            },
            cancellationToken);

        var response = new NotificationTestResponse
        {
            Success = sendResult.Success,
            TotalDevices = sendResult.TotalTokens,
            Successful = sendResult.SuccessfulSends,
            Failed = sendResult.FailedSends
        };

        return Ok(ApiResponse<NotificationTestResponse>.SuccessResponse(
            response,
            sendResult.ErrorMessage ?? "Test notification processed."));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out userId);
    }

    private UnauthorizedObjectResult InvalidToken()
        => Unauthorized(ApiResponse<object>.Failure(
            "User ID claim is missing or invalid in the token.",
            StatusCodes.Status401Unauthorized));
}
