using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages the inbound warehouse receipt process, quality control receiving, and measurement verification.
    /// </summary>
    [ApiController]
    [Route("api/v1/warehouse-receipts")]
    [Authorize]
    public class WarehouseReceiptController : ControllerBase
    {
        private readonly IWarehouseReceiptService _receiptService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WarehouseReceiptController"/> class.
        /// </summary>
        /// <param name="receiptService">The service used to manage warehouse receipts.</param>
        public WarehouseReceiptController(IWarehouseReceiptService receiptService)
        {
            _receiptService = receiptService;
        }

        /// <summary>
        /// Process inbound Quality Control (QC) check when cargo is dropped off at the warehouse.
        /// </summary>
        /// <remarks>
        /// Registers temperature readings, deliverer name, and QC notes for a shipment arrival.
        /// 
        /// Business purpose:
        /// Verify the integrity of the cold chain when cargo physically arrives at a warehouse.
        /// 
        /// Required roles:
        /// Authenticated user (typically WarehouseOperator, Supervisor, or Admin).
        /// 
        /// Workflow impact:
        /// Creates a Warehouse Receipt record in DRAFT status, initiating the inbound flow.
        /// </remarks>
        /// <param name="orderId">The unique identifier of the transport order.</param>
        /// <param name="warehouseId">The unique identifier of the warehouse where cargo is dropped off.</param>
        /// <param name="request">The QC details including deliverer name, temperature, and quality notes.</param>
        /// <returns>The newly created warehouse receipt details.</returns>
        [HttpPost("orders/{orderId:guid}/qc")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseReceiptResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ProcessInboundQC(
            [FromRoute] Guid orderId,
            [FromQuery] Guid warehouseId,
            [FromBody] InboundQCRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var receiverId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token"));

            var result = await _receiptService.ProcessInboundQCAsync(orderId, warehouseId, request, receiverId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update actual measurements of the transport order items during inbound processing.
        /// </summary>
        /// <remarks>
        /// Updates physical dimensions (CBM, dimensions, weight, batch numbers, country of origin, categories) of received cargo items.
        /// 
        /// Business purpose:
        /// Record the real dimensions and product profiles (FEFO dates, batches) in the system.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Populates receipt item records, which will determine storage space allocation.
        /// </remarks>
        /// <param name="orderId">The unique identifier of the transport order.</param>
        /// <param name="request">The actual physical measurements of all items.</param>
        /// <returns>The updated warehouse receipt details.</returns>
        [HttpPut("orders/{orderId:guid}/measurements")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseReceiptResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
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
        /// Finalizes the inbound receipt, locking item records, generating a receipt PDF, and putting items into stock.
        /// 
        /// Business purpose:
        /// Authoritatively complete the cargo check-in and declare the stock available for storage.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Generates a PDF receipt document, updates invoice billing lines, and releases stock to put-away suggestion flow.
        /// </remarks>
        /// <param name="orderId">The unique identifier of the transport order.</param>
        /// <returns>The finalized warehouse receipt details.</returns>
        [HttpPost("orders/{orderId:guid}/completion")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseReceiptResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CompleteInbound([FromRoute] Guid orderId)
        {
            var result = await _receiptService.CompleteInboundAsync(orderId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
