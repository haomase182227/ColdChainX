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
    /// [DEPRECATED] Nghiệm thu ePOD và chốt COD — dùng endpoint mới bên dưới thay thế.
    /// </summary>
    [HttpPost("/api/orders/{orderId:guid}/epods")]
    [Authorize(Roles = "Driver")]
    [Obsolete("Use POST /api/stops/{stopId}/handover-confirmations then POST /api/epods/{epodId}/payments instead.")]
    [ProducesResponseType(typeof(ApiResponse<EpodConfirmResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEpodDelivery(Guid orderId, [FromBody] EpodConfirmRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        request.OrderId = orderId;
        var command = new ConfirmEpodDeliveryCommand { Request = request, UserId = userId };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// [BƯỚC 2] Nghiệm thu hàng và ký nhận tại điểm dừng.
    /// Khách kiểm tra hàng → ký tên → tài xế upload ảnh chữ ký + ảnh bằng chứng hàng lỗi.
    /// Hệ thống cập nhật trạng thái LPN, sinh Biên bản Giao nhận PDF và gửi cảnh báo về kho nếu có hàng trả.
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
    /// [BƯỚC 3] Thu tiền COD sau khi bàn giao hàng xong.
    /// Bắt buộc gọi sau Bước 2 (handover-confirmations). Upload ảnh biên lai (bắt buộc với tiền mặt).
    /// Sinh ePOD hoàn chỉnh và thông báo Admin/Sales/Dispatcher.
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
