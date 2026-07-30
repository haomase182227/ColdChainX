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
    /// Tra cứu thông tin phương tiện (Xe tải &amp; IoT khoang lạnh), tài xế chạy chuyến, khách hàng và tóm tắt các đơn hàng trong một chuyến đi (Trip).
    /// </summary>
    [HttpGet("trips/{tripId:guid}/customer-orders")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TripOrderCustomersResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTripOrderCustomers(Guid tripId)
    {
        var query = new GetTripOrderCustomersQuery { TripId = tripId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Tra cứu bộ chứng từ của chuyến đi / điểm trả hàng dành cho Khách hàng (Customer) khi tài xế giao xe tới.
    /// </summary>
    [HttpGet("trips/{tripId:guid}/documents")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TripDocumentsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTripDocuments(Guid tripId, [FromQuery] Guid? customerId = null)
    {
        // Cho phép Khách hàng xem nhanh bộ chứng từ (E-Waybill, ePOD, Bill chuyển khoản) của Chuyến xe, chỉ cần truyền TripId và CustomerId
        Guid? targetCustomerId = customerId;
        if (!targetCustomerId.HasValue)
        {
            var cidClaim = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(cidClaim) && Guid.TryParse(cidClaim, out var cid))
            {
                targetCustomerId = cid;
            }
        }

        var query = new GetTripDocumentsQuery
        {
            TripId = tripId,
            StopId = null,
            CustomerId = targetCustomerId
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Tra cứu bộ chứng từ của chuyến đi / điểm trả hàng dành cho Khách hàng đăng nhập (CustomerId được tự động lấy từ Token, không cần điền tham số).
    /// </summary>
    [HttpGet("trips/{tripId:guid}/my-documents")]
    [ProducesResponseType(typeof(ApiResponse<TripDocumentsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyTripDocuments(Guid tripId)
    {
        var cidClaim = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(cidClaim) || !Guid.TryParse(cidClaim, out var customerId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Không tìm thấy thông tin Khách hàng (CustomerId) hợp lệ trong Token."));
        }

        var query = new GetTripDocumentsQuery
        {
            TripId = tripId,
            StopId = null,
            CustomerId = customerId
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Cắt chì kẹp seal để mở cửa xe dỡ hàng LIFO và TỰ ĐỘNG TẮT CẢNH BÁO AI (ngăn hệ thống gửi cảnh báo nhiệt độ / mở cửa trong quá trình hạ tải).
    /// </summary>
    [HttpPost("trips/{tripId:guid}/seals/cut")]
    [ProducesResponseType(typeof(ApiResponse<CutSealResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CutSeal(Guid tripId, [FromBody] CutSealRequest request)
    {
        var command = new CutSealCommand
        {
            TripId = tripId,
            StopId = request.StopId
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Đóng kẹp chì (Seal) mới cho chuyến xe sau khi dỡ xong hàng của điểm dừng và chuẩn bị xuất phát tới điểm giao hàng tiếp theo (Hệ thống giao hàng ghép LTL).
    /// Khi gọi API này: (1) Thiết bị IoT lập tức nhận lệnh MQTT "START_STREAMING" và gửi lại telemetry. (2) Do mới đóng cửa xe và nhiệt độ chưa ổn định, bộ giám sát AI sẽ tạo vùng đệm (Cooling Recovery Window) tạm miễn gửi các thông báo cảnh báo nhiệt trong 15 phút đầu tiên!
    /// </summary>
    [HttpPost("trips/{tripId:guid}/seals/apply")]
    [ProducesResponseType(typeof(ApiResponse<ApplySealResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplySeal(Guid tripId, [FromBody] ApplySealRequest request)
    {
        var command = new ApplySealCommand
        {
            TripId = tripId,
            SealCode = request.SealCode
        };
        var result = await _mediator.Send(command);
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
    /// Check-in khi tài xế mang xe tới bãi/điểm giao hàng. Hệ thống tự động truy xuất tọa độ GPS thời gian thực từ thiết bị IoT (TelemetryLogs) để xác thực bán kính (< 700m), đồng thời cho phép tải lên file ảnh minh chứng đỗ bãi.
    /// </summary>
    [HttpPost("/api/stops/{stopId:guid}/check-ins")]
    [Authorize(Roles = "Driver")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<CheckinDriverResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckinDriver(Guid stopId, [FromForm] CheckinDriverRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));
        }

        var command = new CheckinDriverCommand
        {
            ProofImageFile = request.ProofImageFile,
            StopId = stopId,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Nghiệm thu bàn giao tay tại điểm hạ hàng, tải lên ảnh chụp Phiếu Giao Hàng/E-Waybill có chữ ký giấy và hợp thức hóa thành ePOD.
    /// </summary>
    [HttpPost("/api/Delivery/stops/{stopId:guid}/confirm-handover")]
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
    /// Lấy thông tin chứng từ giao hàng (ePOD) theo OrderId.
    /// </summary>
    [HttpGet("/api/Delivery/orders/{orderId:guid}/epod")]
    [ProducesResponseType(typeof(ApiResponse<EpodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEpodByOrderId(Guid orderId)
    {
        var query = new GetEpodByOrderIdQuery { OrderId = orderId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Tạo mã QR thanh toán PayOS dựa trên EpodId.
    /// </summary>
    [HttpGet("/api/Delivery/epods/{epodId:guid}/payment-qr")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEpodPaymentQr(Guid epodId)
    {
        var query = new GetEpodPaymentQrQuery { EpodId = epodId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Đồng kiểm OS&D, tính lại Dynamic COD thực thu và phát lệnh trả hàng (Reverse Logistics).
    /// </summary>
    [HttpPost("/api/epods/{epodId:guid}/dynamic-cod")]
    [Authorize(Roles = "Driver,Dispatcher,Admin")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessDynamicCod(Guid epodId, [FromBody] ProcessDynamicCodCommand command)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        command.EpodId = epodId;
        command.UserId = userId;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// [Sau Bước 3 Ký Chốt Sổ] Thanh toán COD tại Dock theo số lượng Thực Nhận (sau sự cố OS&D) và tạo bút toán IN vào sổ cái.
    /// </summary>
    [HttpPost("/api/epods/{epodId:guid}/pay-actual-received")]
    [Authorize(Roles = "Driver,Dispatcher,Admin,Customer")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PayActualReceived(Guid epodId, [FromBody] PayActualReceivedCodCommand command)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        command.EpodId = epodId;
        command.UserId = userId;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// [Bước 3.2 Xác nhận thanh toán & Chụp bill] Kiểm tra xem hệ thống PayOS đã "tinh tinh" nhận tiền chưa và đính kèm ảnh chụp màn hình chuyển khoản của Khách hàng.
    /// </summary>
    [HttpPost("/api/epods/{epodId:guid}/verify-qr-payment")]
    [Authorize(Roles = "Driver,Dispatcher,Admin,Customer")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyQrPayment([FromRoute] Guid epodId, [FromForm] ColdChainX.Application.DTOs.Delivery.VerifyQrPaymentRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new VerifyQrPaymentCommand
        {
            EpodId = epodId,
            UserId = userId,
            Request = request
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }



    /// <summary>
    /// Handle driver COD handover at the end of trip.
    /// </summary>
    [HttpPost("/api/trips/{tripId:guid}/cod-handovers")]
    [Authorize(Roles = "Admin,Manager, WarehouseWorker, Dispatcher")]
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
    /// Đối soát tài chính chốt chuyến (COD + Phí neo xe), tự động sinh hóa đơn phạt nếu hụt và kết nối MISA/SAP ERP.
    /// </summary>
    [HttpPost("/api/trips/{tripId:guid}/reconcile")]
    [Authorize(Roles = "Admin,Dispatcher,Accountant")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReconcileTripFinances(Guid tripId, [FromBody] ColdChainX.Application.Features.Accounting.Commands.ReconcileTripFinancesCommand command)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        command.TripId = tripId;
        command.AccountantUserId = userId;
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
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
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
            EvidenceImageUrl = request.EvidenceImageUrl,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("{stopId}/report-no-show")]
    public async Task<IActionResult> ReportNoShow(Guid stopId, [FromBody] ReportNoShowRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new ReportNoShowCommand
        {
            TripStopId = stopId,
            DriverId = userId,
            EvidenceImageUrl = request.EvidenceImageUrl
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
