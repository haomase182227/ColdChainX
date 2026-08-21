using ColdChainX.API.Authorization;
using ColdChainX.Application.Features.Inbound.Commands;
using ColdChainX.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
    [HasPermission(PermissionCodes.WarehouseQcInspect)]
    public async Task<IActionResult> ProcessQc([FromForm] ProcessInboundQcRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var receiverId))
            return Unauthorized(new { Message = "Invalid or missing user token." });

        var command = new ProcessInboundQcCommand
        {
            AsnId = request.AsnId ?? Guid.Empty,
            ActualWeightKg = request.ActualWeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            ActualPackageLinesJson = request.ActualPackageLinesJson,
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
    [HasPermission(PermissionCodes.WarehouseQcInspect)]
    public async Task<IActionResult> ReEvaluateQc([FromForm] ColdChainX.Application.DTOs.WarehouseFlow.ReEvaluateInboundQcRequest request)
    {
        var warehouseIdClaim = User.FindFirst("WarehouseId")?.Value;
        Guid.TryParse(warehouseIdClaim, out var warehouseId);

        var command = new ReEvaluateInboundQcCommand
        {
            LpnId = request.LpnId,
            ActualPackageLinesJson = request.ActualPackageLinesJson,
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
    [HasPermission(PermissionCodes.WarehouseReceivingConfirm)]
    public async Task<IActionResult> Putaway([FromBody] PutawayLpnCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("receipts")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetReceipts([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GetInboundReceiptsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    [HttpGet("receipts/{id}")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetReceipt(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GetInboundReceiptDetailQuery(id));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("receipts/{id}/pdf")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
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
    [HasPermission(PermissionCodes.WarehouseReceivingConfirm)]
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
    
    [HttpPost("reverse")]
    public async Task<IActionResult> ProcessReverse([FromBody] ColdChainX.Application.DTOs.WarehouseFlow.ProcessInboundReverseRequest request)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(ColdChainX.Shared.Responses.ApiResponse<object>.Failure("Unauthorized."));

        var command = new ColdChainX.Application.Features.Inbound.Commands.ProcessInboundReverseCommand
        {
            WarehouseId = request.WarehouseId,
            UserId = userId,
            LpnCodes = request.LpnCodes,
            DriverId = request.DriverId,
            VehicleId = request.VehicleId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpGet("lookup/return-slips")]
    public async Task<IActionResult> LookupReturnSlips()
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GetPendingReturnSlipsQuery());
        return Ok(result);
    }

    [HttpPost("disposition")]
    [Authorize(Roles = "Admin,Dispatcher,WarehouseWorker")]
    public async Task<IActionResult> ProcessDisposition([FromForm] ColdChainX.Application.Features.Warehouse.Commands.ProcessInboundDispositionCommand command)
    {
        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
