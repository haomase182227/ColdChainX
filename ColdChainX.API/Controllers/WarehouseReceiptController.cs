using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/warehouse-receipts")]
    [Authorize]
    public class WarehouseReceiptController : ControllerBase
    {
        private readonly IWarehouseReceiptService _receiptService;

        public WarehouseReceiptController(IWarehouseReceiptService receiptService)
        {
            _receiptService = receiptService;
        }

        [HttpPost("qc-receive/{orderId:guid}")]
        public async Task<IActionResult> ProcessInboundQC(
            [FromRoute] Guid orderId,
            [FromQuery] Guid warehouseId,
            [FromBody] InboundQCRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var receiverId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _receiptService.ProcessInboundQCAsync(orderId, warehouseId, request, receiverId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPut("measurements/{orderId:guid}")]
        public async Task<IActionResult> UpdateMeasurements(
            [FromRoute] Guid orderId,
            [FromBody] UpdateMeasurementsRequest request)
        {
            var result = await _receiptService.UpdateMeasurementsAsync(orderId, request);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("complete/{orderId:guid}")]
        public async Task<IActionResult> CompleteInbound([FromRoute] Guid orderId)
        {
            var result = await _receiptService.CompleteInboundAsync(orderId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
