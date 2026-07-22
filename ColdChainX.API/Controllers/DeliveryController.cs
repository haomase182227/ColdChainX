using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Application.Features.Delivery.Commands;
using ColdChainX.Application.Features.Delivery.Queries;
using ColdChainX.Shared.Responses;

using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeliveryController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileService _fileService;

    public DeliveryController(IMediator mediator, IFileService fileService)
    {
        _mediator = mediator;
        _fileService = fileService;
    }

    /// <summary>
    /// Get delivery progress and LPN list for a trip.
    /// </summary>
    [HttpGet("trips/{tripId:guid}/lpns")]
    [ProducesResponseType(typeof(ApiResponse<TripDeliveryProgressResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTripDeliveryProgress(Guid tripId)
    {
        var query = new GetTripDeliveryProgressQuery { TripId = tripId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get delivery confirmation detail for a specific LPN.
    /// </summary>
    [HttpGet("trips/{tripId:guid}/lpns/{lpnId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LpnDeliveryStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLpnDeliveryDetail(Guid tripId, Guid lpnId)
    {
        var query = new GetLpnDeliveryDetailQuery { TripId = tripId, LpnId = lpnId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Check in driver, validate GPS, and cut the old seal.
    /// </summary>
    [HttpPost("/api/stops/{stopId:guid}/check-ins")]
    [Authorize(Roles = "Driver")]
    [ProducesResponseType(typeof(ApiResponse<CheckinDriverResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckinDriver(Guid stopId, [FromBody] CheckinDriverRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));
        }

        var command = new CheckinDriverCommand
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            StopId = stopId,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }


    /// <summary>
    /// Confirm handover at stop, upload signature, and handle partial returns.
    /// </summary>
    [HttpPost("/api/stops/{stopId:guid}/handover-confirmations")]
    [Authorize(Roles = "Driver")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<HandoverConfirmResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmHandover(Guid stopId, [FromForm] HandoverConfirmRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new ConfirmHandoverCommand
        {
            StopId = stopId,
            Request = request,
            UserId = userId
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Record COD payment (Cash or QR).
    /// </summary>
    [HttpPost("/api/epods/{epodId:guid}/payments")]
    [Authorize(Roles = "Driver")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<RecordCodPaymentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordCodPayment(Guid epodId, [FromForm] RecordCodPaymentRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new RecordCodPaymentCommand
        {
            EpodId = epodId,
            Request = request,
            UserId = userId
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Depart stop and apply a new seal.
    /// </summary>
    [HttpPost("/api/stops/{stopId:guid}/departures")]
    [Authorize(Roles = "Driver")]
    [ProducesResponseType(typeof(ApiResponse<DepartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DepartStop(Guid stopId, [FromBody] DepartRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));
        }

        var command = new DepartStopCommand
        {
            StopId = stopId,
            NewSealCode = request.NewSealCode,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Handle driver COD handover at the end of trip.
    /// </summary>
    [HttpPost("/api/trips/{tripId:guid}/cod-handovers")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(ApiResponse<CodHandoverResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HandoverCod(Guid tripId, [FromBody] CodHandoverRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));
        }

        var command = new HandoverCodCommand
        {
            TripId = tripId,
            Request = request,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Upload an image to Cloudinary.
    /// </summary>
    [HttpPost("/api/deliveries/upload-image")]
    [Authorize(Roles = "Driver,Admin,Manager")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<string>.Failure("Vui lòng chọn tệp tin ảnh."));
        }

        try
        {
            var url = await _fileService.UploadFileAsync(file);
            return Ok(ApiResponse<string>.SuccessResponse(url, "Tải ảnh lên thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<string>.Failure($"Lỗi khi tải ảnh: {ex.Message}"));
        }
    }

    /// <summary>
    /// Update GPS Location and trigger Geofence ETA Notification
    /// </summary>
    [HttpPost("trips/{tripId:guid}/location")]
    [Authorize(Roles = "Driver")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateLocation(Guid tripId, [FromBody] UpdateLocationRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new UpdateLocationCommand
        {
            TripId = tripId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Mark delivery as failed after waiting 30 mins
    /// </summary>
    [HttpPost("stops/{stopId:guid}/failed-delivery")]
    [Authorize(Roles = "Driver")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MarkFailedDelivery(Guid stopId, [FromBody] MarkFailedDeliveryRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new MarkFailedDeliveryCommand
        {
            StopId = stopId,
            Reason = request.Reason,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
