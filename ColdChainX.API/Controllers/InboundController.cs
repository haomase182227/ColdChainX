using ColdChainX.Application.Features.Inbound.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InboundController : ControllerBase
{
    private readonly IMediator _mediator;

    public InboundController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("qc")]
    [Authorize]
    public async Task<IActionResult> ProcessQc([FromForm] ProcessInboundQcRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var receiverId))
            return Unauthorized(new { Message = "Invalid or missing user token." });

        var command = new ProcessInboundQcCommand
        {
            AsnId = request.AsnId,
            ActualWeightKg = request.ActualWeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            Temperature = request.Temperature,
            EvidenceImages = request.EvidenceImages,
            ReceiverId = receiverId,
            WarehouseId = Guid.Empty
        };

        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);
            
        return Ok(result);
    }

    [HttpPut("qc/re-evaluate")]
    [Authorize]
    public async Task<IActionResult> ReEvaluateQc([FromForm] ColdChainX.Application.DTOs.WarehouseFlow.ReEvaluateInboundQcRequest request)
    {
        var warehouseIdClaim = User.FindFirst("WarehouseId")?.Value;
        Guid.TryParse(warehouseIdClaim, out var warehouseId);

        var command = new ReEvaluateInboundQcCommand
        {
            LpnId = request.LpnId,
            ActualWeightKg = request.ActualWeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            Temperature = request.Temperature,
            EvidenceImages = request.EvidenceImages,
            WarehouseId = warehouseId
        };

        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);
            
        return Ok(result);
    }

    [HttpPost("putaway")]
    public async Task<IActionResult> Putaway([FromBody] PutawayLpnCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts()
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GetInboundReceiptsQuery());
        return Ok(result);
    }

    [HttpGet("receipts/{id}")]
    public async Task<IActionResult> GetReceipt(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GetInboundReceiptDetailQuery(id));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("receipts/{id}/pdf")]
    public async Task<IActionResult> GetReceiptPdf(Guid id)
    {
        try
        {
            var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GenerateReceiptPdfQuery(id));
            return File(pdfBytes, "application/pdf", $"PhieuNhapKho_{id.ToString().Substring(0, 8)}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
    [HttpPost("receipts/generate")]
    [Authorize]
    public async Task<IActionResult> GenerateReceipt([FromBody] GenerateWarehouseReceiptRequest request)
    {
        var command = new GenerateWarehouseReceiptCommand
        {
            AsnId = request.AsnId,
            DelivererName = request.DelivererName,
            VehiclePlate = request.VehiclePlate,
            Note = request.Note
        };

        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}
