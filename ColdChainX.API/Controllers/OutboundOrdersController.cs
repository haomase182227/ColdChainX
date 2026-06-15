using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Outbound;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/outbound-orders")]
    [Authorize]
    public class OutboundOrdersController : ControllerBase
    {
        private readonly IOutboundOrderService _outboundOrderService;

        public OutboundOrdersController(IOutboundOrderService outboundOrderService)
        {
            _outboundOrderService = outboundOrderService;
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var result = await _outboundOrderService.GetListAsync(pageNumber, pageSize, search);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _outboundOrderService.GetByIdAsync(id);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPut("{id:guid}")]
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

        [HttpPost("{id:guid}/allocate")]
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

        [HttpPost("{id:guid}/cancel")]
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

        [HttpPost("{id:guid}/start-picking")]
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

        [HttpPost("{id:guid}/complete-picking")]
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

        [HttpPost("{id:guid}/ship")]
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
