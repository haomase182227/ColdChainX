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

        /// <summary>
        /// Process inbound Quality Control (QC) check when cargo is dropped off at the warehouse.
        /// </summary>
        /// <remarks>
        /// Instructions:
        /// - Authenticate using your JWT token before calling this endpoint.
        /// - Specify the Order ID in the route, the Warehouse ID in the query string, and the QC report (deliverer name, actual temperature, notes, and individual item actual quantities) in the request body.
        /// - Validates that the order exists, is in the correct state, and the warehouse exists.
        /// - Creates a new Warehouse Receipt and records individual receipt item actual quantities and conditions.
        /// </remarks>
        /// <param name="orderId">The unique identifier of the transport order.</param>
        /// <param name="warehouseId">The unique identifier of the warehouse where cargo is dropped off.</param>
        /// <param name="request">The QC details including deliverer name, temperature, and actual quantities/conditions for items.</param>
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

        /// <summary>
        /// Update actual measurements of the transport order items during inbound processing.
        /// </summary>
        /// <remarks>
        /// Instructions:
        /// - Authenticate using your JWT token before calling this endpoint.
        /// - Specify the Order ID in the route.
        /// - Provide the actual measurements (actual CBM/volume and actual weight) in the request body.
        /// - This updates the transport order with the physical properties verified at the warehouse.
        /// </remarks>
        /// <param name="orderId">The unique identifier of the transport order.</param>
        /// <param name="request">The actual physical measurements (actual CBM and actual weight in KG).</param>
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

        /// <summary>
        /// Complete the inbound warehouse receipt process.
        /// </summary>
        /// <remarks>
        /// Instructions:
        /// - Authenticate using your JWT token before calling this endpoint.
        /// - Specify the Order ID in the route.
        /// - Completes the inbound flow, locks the warehouse receipt, adjusts the invoice line items based on actual quantities, and generates a PDF receipt document.
        /// </remarks>
        /// <param name="orderId">The unique identifier of the transport order.</param>
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
