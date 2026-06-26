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

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeliveryController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeliveryController(IMediator mediator)
    {
        _mediator = mediator;
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
    /// Driver xác nhận giao hàng thành công (Accepted) cho một LPN cụ thể.
    /// </summary>
    [HttpPost("trips/{tripId:guid}/lpns/{lpnId:guid}/confirm")]
    [Authorize(Roles = "Driver")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<LpnDeliveryStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmLpnDelivery(
        Guid tripId, Guid lpnId, [FromForm] ConfirmLpnDeliveryRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));
        }

        var command = new ConfirmLpnDeliveryCommand
        {
            TripId = tripId,
            LpnId = lpnId,
            ReceiverName = request.ReceiverName,
            ReceiverPhone = request.ReceiverPhone,
            EvidenceImage = request.EvidenceImage,
            UserId = userId,
            CheckinAt = request.CheckinAt,
            SignatureImage = request.SignatureImage,
            CodAmount = request.CodAmount,
            CodPaymentMethod = request.CodPaymentMethod,
            CodReceiptImage = request.CodReceiptImage,
            NewSealNumber = request.NewSealNumber
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Driver từ chối nhận hàng (Rejected) cho một LPN cụ thể.
    /// </summary>
    [HttpPost("trips/{tripId:guid}/lpns/{lpnId:guid}/reject")]
    [Authorize(Roles = "Driver")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<LpnDeliveryStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectLpnDelivery(
        Guid tripId, Guid lpnId, [FromForm] RejectLpnDeliveryRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure("Unauthorized."));
        }

        var command = new RejectLpnDeliveryCommand
        {
            TripId = tripId,
            LpnId = lpnId,
            RejectReason = request.RejectReason,
            RejectNote = request.RejectNote,
            EvidenceImage = request.EvidenceImage,
            UserId = userId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
