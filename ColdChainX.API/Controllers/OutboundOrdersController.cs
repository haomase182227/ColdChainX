using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Outbound;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages the full lifecycle of outbound orders in the Cold Chain WMS.
    /// </summary>
    /// <remarks>
    /// An outbound order represents a customer dispatch request. The typical workflow is:
    /// <list type="number">
    ///   <item><description>Create the order (status: Pending).</description></item>
    ///   <item><description>Allocate stock using FEFO (First-Expiry First-Out).</description></item>
    ///   <item><description>Assign a picker and start picking.</description></item>
    ///   <item><description>Complete picking when all items are collected.</description></item>
    ///   <item><description>Ship the order to hand off to the transport module.</description></item>
    /// </list>
    /// An order can be cancelled at any stage before shipping.
    /// All write operations require a valid JWT bearer token.
    /// </remarks>
    [ApiController]
    [Route("api/v1/outbound-orders")]
    [Authorize]
    public class OutboundOrdersController : ControllerBase
    {
        private readonly IOutboundOrderService _outboundOrderService;

        /// <summary>
        /// Initialises a new instance of <see cref="OutboundOrdersController"/>.
        /// </summary>
        /// <param name="outboundOrderService">Service handling outbound order business logic.</param>
        public OutboundOrdersController(IOutboundOrderService outboundOrderService)
        {
            _outboundOrderService = outboundOrderService;
        }

        /// <summary>
        /// Creates a new outbound order.
        /// </summary>
        /// <remarks>
        /// Registers a customer dispatch request. The order is created with status <c>Pending</c>
        /// and must be allocated before picking can begin.
        /// The <c>userId</c> extracted from the JWT token is recorded as the order creator.
        /// </remarks>
        /// <param name="request">Order details including receiver info, destination address, and requested items.</param>
        /// <returns>The created outbound order with its system-assigned ID and order code.</returns>
        /// <response code="200">Order created successfully. Returns <see cref="OutboundOrderResponse"/>.</response>
        /// <response code="400">Validation failed or business rule was violated (e.g. unknown SKU).</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpPost]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Create([FromBody] CreateOutboundOrderRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _outboundOrderService.CreateAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Returns a paginated list of outbound orders with optional filters.
        /// </summary>
        /// <remarks>
        /// Supports filtering by order code keyword, order status, and customer.
        /// Results are sorted by creation date descending (newest first).
        ///
        /// Valid values for <paramref name="status"/>: <c>Pending</c>, <c>Allocated</c>,
        /// <c>Picking</c>, <c>Picked</c>, <c>Shipped</c>, <c>Cancelled</c>.
        /// </remarks>
        /// <param name="pageNumber">Page number to retrieve (1-based, default: 1).</param>
        /// <param name="pageSize">Number of records per page (default: 10, max: 100).</param>
        /// <param name="search">Optional keyword matched against order code or receiver name.</param>
        /// <param name="status">Optional filter by order lifecycle status.</param>
        /// <param name="customerId">Optional filter to return orders for a specific customer.</param>
        /// <returns>Paginated list of <see cref="OutboundOrderResponse"/>.</returns>
        /// <response code="200">Returns a paged list of outbound orders.</response>
        /// <response code="400">Invalid query parameters.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpGet]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetList(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] Guid? customerId = null)
        {
            var result = await _outboundOrderService.GetListAsync(pageNumber, pageSize, search, status, customerId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Returns the details of a single outbound order by its unique ID.
        /// </summary>
        /// <remarks>
        /// Returns the full order snapshot including all line items, receiver details,
        /// status, and allocation information.
        /// </remarks>
        /// <param name="id">Unique identifier (GUID) of the outbound order.</param>
        /// <returns>Full outbound order details as <see cref="OutboundOrderResponse"/>.</returns>
        /// <response code="200">Order found. Returns <see cref="OutboundOrderResponse"/>.</response>
        /// <response code="400">No order found with the supplied ID.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _outboundOrderService.GetByIdAsync(id);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Updates the receiver and item details of an outbound order.
        /// </summary>
        /// <remarks>
        /// Only orders in <c>Pending</c> status can be updated.
        /// Updating a <c>Allocated</c>, <c>Picking</c>, or later-status order will be rejected.
        /// </remarks>
        /// <param name="id">Unique identifier (GUID) of the outbound order to update.</param>
        /// <param name="request">Updated receiver information and item list.</param>
        /// <returns>The updated outbound order.</returns>
        /// <response code="200">Order updated successfully. Returns <see cref="OutboundOrderResponse"/>.</response>
        /// <response code="400">Order is not editable at its current status, or validation failed.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpPut("{id:guid}")]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateOutboundOrderRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _outboundOrderService.UpdateAsync(id, request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Allocates stock to an outbound order using FEFO (First-Expiry First-Out) logic.
        /// </summary>
        /// <remarks>
        /// This step reserves inventory against the order line items. Stock is selected automatically
        /// by expiry date (earliest expiry first) to minimise waste. The order status changes from
        /// <c>Pending</c> to <c>Allocated</c> upon success.
        ///
        /// Returns a breakdown of every allocated batch and location as <see cref="AllocationResponse"/>.
        /// </remarks>
        /// <param name="id">Unique identifier (GUID) of the outbound order to allocate.</param>
        /// <returns>FEFO allocation result with batch and location breakdown.</returns>
        /// <response code="200">Stock allocated successfully. Returns <see cref="AllocationResponse"/>.</response>
        /// <response code="400">Insufficient stock, order is not in <c>Pending</c> status, or order not found.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpPost("{id:guid}/allocations")]
        [ProducesResponseType(typeof(AllocationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Allocate([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _outboundOrderService.AllocateOrderAsync(id, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Cancels an outbound order.
        /// </summary>
        /// <remarks>
        /// Any allocated inventory is automatically released back to available stock.
        /// Only orders that have not yet been <c>Shipped</c> can be cancelled.
        /// The order status changes to <c>Cancelled</c>.
        /// </remarks>
        /// <param name="id">Unique identifier (GUID) of the outbound order to cancel.</param>
        /// <returns>Confirmation of cancellation with the updated order record.</returns>
        /// <response code="200">Order cancelled successfully.</response>
        /// <response code="400">Order is already shipped or in a non-cancellable state.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Cancel([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _outboundOrderService.CancelOrderAsync(id, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Assigns a picker and transitions the order from Allocated to Picking.
        /// </summary>
        /// <remarks>
        /// The picker is identified by <paramref name="pickerId"/>. Once this endpoint is called,
        /// the order status changes to <c>Picking</c> and the picking list becomes available.
        /// The order must be in <c>Allocated</c> status to start picking.
        /// </remarks>
        /// <param name="id">Unique identifier (GUID) of the outbound order.</param>
        /// <param name="pickerId">Unique identifier (GUID) of the warehouse picker user.</param>
        /// <returns>Updated outbound order with picking assignment details.</returns>
        /// <response code="200">Picking started successfully. Returns <see cref="OutboundOrderResponse"/>.</response>
        /// <response code="400">Order is not in <c>Allocated</c> status, or picker not found.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpPost("{id:guid}/picking-assignment")]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> StartPicking([FromRoute] Guid id, [FromQuery] Guid pickerId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _outboundOrderService.StartPickingAsync(id, pickerId, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Marks the outbound order as fully picked and ready for shipment.
        /// </summary>
        /// <remarks>
        /// Transitions the order from <c>Picking</c> to <c>Picked</c> status.
        /// After this step, the order awaits hand-off to the transport/dispatch module via
        /// the <c>POST /ship</c> endpoint.
        /// </remarks>
        /// <param name="id">Unique identifier (GUID) of the outbound order.</param>
        /// <returns>Updated outbound order in <c>Picked</c> status.</returns>
        /// <response code="200">Picking completed successfully. Returns <see cref="OutboundOrderResponse"/>.</response>
        /// <response code="400">Order is not currently in <c>Picking</c> status.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpPost("{id:guid}/picking-completion")]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> CompletePicking([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _outboundOrderService.CompletePickingAsync(id, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Marks the order as shipped and hands it off to the transport module.
        /// </summary>
        /// <remarks>
        /// Transitions the order from <c>Picked</c> to <c>Shipped</c> status. This is the final
        /// WMS step. After shipping, the order is considered closed from the warehouse perspective
        /// and cannot be modified or cancelled.
        /// Inventory deductions are finalised at this step.
        /// </remarks>
        /// <param name="id">Unique identifier (GUID) of the outbound order to ship.</param>
        /// <returns>Updated outbound order in <c>Shipped</c> status.</returns>
        /// <response code="200">Order shipped successfully. Returns <see cref="OutboundOrderResponse"/>.</response>
        /// <response code="400">Order is not in <c>Picked</c> status, or order not found.</response>
        /// <response code="401">Bearer token is missing or invalid.</response>
        [HttpPost("{id:guid}/shipment")]
        [ProducesResponseType(typeof(OutboundOrderResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Ship([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _outboundOrderService.ShipOrderAsync(id, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
