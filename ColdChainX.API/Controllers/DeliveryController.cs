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

    [HttpGet("trips/{tripId:guid}/lpns")]
    public async Task<IActionResult> GetTripDeliveryProgress(Guid tripId)
    {
        var query = new GetTripDeliveryProgressQuery { TripId = tripId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("trips/{tripId:guid}/customer-orders")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTripOrderCustomers(Guid tripId)
    {
        var query = new GetTripOrderCustomersQuery { TripId = tripId };
        var result = await _mediator.Send(query);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpGet("trips/{tripId:guid}/documents")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTripDocuments(Guid tripId, [FromQuery] Guid? customerId = null)
    {
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
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpGet("trips/{tripId:guid}/my-documents")]
    public async Task<IActionResult> GetMyTripDocuments(Guid tripId)
    {
        var cidClaim = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(cidClaim) || !Guid.TryParse(cidClaim, out var customerId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Khong tim thay thong tin Khach hang (CustomerId) hop le trong Token."));
        }

        var query = new GetTripDocumentsQuery
        {
            TripId = tripId,
            StopId = null,
            CustomerId = customerId
        };
        var result = await _mediator.Send(query);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpPost("trips/{tripId:guid}/seals/cut")]
    public async Task<IActionResult> CutSeal(Guid tripId, [FromBody] CutSealRequest request)
    {
        var command = new CutSealCommand
        {
            TripId = tripId,
            StopId = request.StopId
        };
        var result = await _mediator.Send(command);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpPost("trips/{tripId:guid}/seals/apply")]
    public async Task<IActionResult> ApplySeal(Guid tripId, [FromBody] ApplySealRequest request)
    {
        var command = new ApplySealCommand
        {
            TripId = tripId,
            SealCode = request.SealCode
        };
        var result = await _mediator.Send(command);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpGet("trips/{tripId:guid}/lpns/{lpnId:guid}")]
    public async Task<IActionResult> GetLpnDeliveryDetail(Guid tripId, Guid lpnId)
    {
        var query = new GetLpnDeliveryDetailQuery { TripId = tripId, LpnId = lpnId };
        var result = await _mediator.Send(query);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpPost("/api/stops/{stopId:guid}/check-ins")]
    [Authorize(Roles = "Driver")]
    [Consumes("multipart/form-data")]
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
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpPost("/api/Delivery/depart")]
    public async Task<IActionResult> CloseShift([FromBody] CloseShiftCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpPost("/api/Delivery/stops/{stopId:guid}/confirm-handover")]
    [Authorize(Roles = "Driver")]
    [Consumes("multipart/form-data")]
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
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpGet("/api/Delivery/orders/{orderId:guid}/epod")]
    public async Task<IActionResult> GetEpodByOrderId(Guid orderId)
    {
        var query = new GetEpodByOrderIdQuery { OrderId = orderId };
        var result = await _mediator.Send(query);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpGet("/api/Delivery/epods/{epodId:guid}/payment-qr")]
    public async Task<IActionResult> GetEpodPaymentQr(Guid epodId)
    {
        var query = new GetEpodPaymentQrQuery { EpodId = epodId };
        var result = await _mediator.Send(query);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpPost("/api/Delivery/stops/{stopId:guid}/dynamic-cod")]
    [Authorize(Roles = "Driver,Dispatcher,Admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ProcessDynamicCod(Guid stopId, [FromForm] ColdChainX.Application.DTOs.Delivery.ProcessDynamicCodRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new ColdChainX.Application.Features.Delivery.Commands.ProcessDynamicCodCommand
        {
            StopId = stopId,
            TripId = request.TripId,
            CustomerId = request.CustomerId,
            UserId = userId,
            RejectedQuantity = request.RejectedQuantity,
            RejectionReason = request.RejectionReason,
            IsReturnToWarehouse = request.IsReturnToWarehouse,
            EvidenceImageFile = request.EvidenceImageFile
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpPost("/api/Delivery/stops/{stopId:guid}/reject-entire-lpn")]
    [Authorize(Roles = "Driver,Dispatcher,Admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RejectEntireLpn(Guid stopId, [FromForm] ColdChainX.Application.DTOs.Delivery.RejectEntireLpnRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new ColdChainX.Application.Features.Delivery.Commands.RejectEntireLpnCommand
        {
            StopId = stopId,
            TripId = request.TripId,
            CustomerId = request.CustomerId,
            UserId = userId,
            RejectionReason = request.RejectionReason,
            IsReturnToWarehouse = request.IsReturnToWarehouse,
            EvidenceImageFile = request.EvidenceImageFile
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }


    [HttpPost("/api/epods/{epodId:guid}/verify-qr-payment")]
    [Authorize(Roles = "Driver,Dispatcher,Admin,Customer")]
    [Consumes("multipart/form-data")]
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
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }



    [HttpPost("/api/trips/{tripId:guid}/cod-handovers")]
    [Authorize(Roles = "Admin,WarehouseWorker,Dispatcher")]
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
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }



    [HttpPost("/api/deliveries/upload-image")]
    [Authorize(Roles = "Driver,Admin,WarehouseWorker")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<string>.Failure("Vui long chon tep tin anh."));
        }

        try
        {
            var url = await _fileService.UploadFileAsync(file);
            return Ok(ApiResponse<string>.SuccessResponse(url, "Tai anh len thanh cong."));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<string>.Failure($"Loi khi tai anh: {ex.Message}"));
        }
    }

    [HttpPost("trips/{tripId:guid}/location")]
    [Authorize(Roles = "Driver")]
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
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }


    [HttpPost("{stopId:guid}/report-no-show")]
    [Authorize(Roles = "Driver,Admin,Dispatcher")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ReportNoShow(Guid stopId, [FromForm] ReportNoShowRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));

        var command = new ReportNoShowCommand
        {
            TripStopId = stopId,
            DriverId = userId,
            EvidenceImageFile = request.EvidenceImageFile
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }

    [HttpGet("/api/Delivery/nearest-return-warehouses")]
    [Authorize(Roles = "Driver,Dispatcher,Admin,WarehouseWorker")]
    public async Task<IActionResult> GetNearestReturnWarehouses([FromQuery] Guid tripId)
    {
        if (tripId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Failure("Driver coordinates required or invalid search radius (Coordinates and positive search distance required).", 400));

        var query = new ColdChainX.Application.Features.Delivery.Queries.GetNearestReturnWarehousesQuery
        {
            TripId = tripId
        };

        var result = await _mediator.Send(query);
        if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
        return Ok(result);
    }
}
