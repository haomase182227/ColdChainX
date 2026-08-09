using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/warehouses")]
    [Authorize]
    public class WarehouseController : ControllerBase
    {
        private readonly IWarehouseService _warehouseService;

        public WarehouseController(IWarehouseService warehouseService)
        {
            _warehouseService = warehouseService;
        }

        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseWorker")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Create([FromBody] CreateWarehouseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _warehouseService.CreateAsync(request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin,WarehouseWorker")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWarehouseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _warehouseService.UpdateAsync(id, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _warehouseService.DeleteAsync(id, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _warehouseService.GetByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<WarehouseResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetList(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            if (pageNumber <= 0 || pageSize <= 0)
                return BadRequest(ApiResponse<object>.Failure("PageNumber and PageSize must be greater than zero."));

            var result = await _warehouseService.GetListAsync(pageNumber, pageSize, search);
            return Ok(result);
        }

        [HttpGet("{warehouseId}/lpns")]
        public async Task<ActionResult<ApiResponse<PagedResult<LpnResponse>>>> GetLpnsInWarehouse(
            [FromRoute] Guid warehouseId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (page <= 0 || pageSize <= 0)
                return BadRequest(ApiResponse<object>.Failure("Page and PageSize must be greater than zero."));

            var response = await _warehouseService.GetLpnsInWarehouseAsync(warehouseId, page, pageSize);
            return Ok(response);
        }
    }
}
