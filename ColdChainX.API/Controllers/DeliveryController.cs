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
    /// Lấy danh sách LPN và tiến độ giao hàng của một chuyến xe (Trip).
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
    /// Lấy chi tiết thông tin xác nhận giao hàng của một LPN cụ thể trên chuyến đi.
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
    /// Driver thực hiện check-in tại Stop đích (đối chiếu tọa độ GPS và cắt chì cũ).
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
    /// Nghiệm thu ePOD và chốt COD của Đơn hàng (Door Delivery & COD Confirm).
    /// </summary>
    [HttpPost("/api/orders/{orderId:guid}/epods")]
    [Authorize(Roles = "Driver")]
    [ProducesResponseType(typeof(ApiResponse<EpodConfirmResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmEpodDelivery(Guid orderId, [FromBody] EpodConfirmRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));
        }

        request.OrderId = orderId;
        var command = new ConfirmEpodDeliveryCommand
        {
            Request = request,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Rời điểm dừng và kẹp chì chặng mới (Depart & Re-seal).
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
    /// Quyết toán COD của Tài xế (COD Handover).
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
    /// Upload ảnh lên Cloudinary để lấy URL chèn vào ePOD / Bằng chứng lỗi.
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
}
