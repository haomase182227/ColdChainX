using ColdChainX.Application.DTOs.Asns;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/asns")]
    public class AsnController : ControllerBase
    {
        private readonly IAsnService _asnService;

        public AsnController(IAsnService asnService)
        {
            _asnService = asnService;
        }

        /// <summary>
        /// Retrieve list of scheduled inbound deliveries (ASNs), with filtering and paging.
        /// </summary>
        /// <remarks>
        /// For Customer role: Only returns ASNs belonging to the authenticated customer.
        /// For Admin/Manager/WarehouseOperator roles: Returns all ASNs with optional filters.
        /// </remarks>
        /// <param name="status">Optional status filter (e.g. SCHEDULED, ARRIVED).</param>
        /// <param name="dateFrom">Optional start date range for requested drop-off time.</param>
        /// <param name="dateTo">Optional end date range for requested drop-off time.</param>
        /// <param name="searchQuery">Optional search term matching code, tracking code, item name, customer name, or address.</param>
        /// <param name="warehouseId">Optional warehouse filter matching link or destination address.</param>
        /// <param name="orderId">Optional order filter.</param>
        /// <param name="customerId">Optional customer filter (available only to Admin/Manager/Operator).</param>
        /// <param name="pageNumber">Page index (1-based).</param>
        /// <param name="pageSize">Size of each page.</param>
        /// <returns>A paginated list of scheduled inbound deliveries.</returns>
        [HttpGet]
        [Authorize]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<InboundScheduleResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetInboundSchedules(
            [FromQuery] string? status,
            [FromQuery] DateTime? dateFrom,
            [FromQuery] DateTime? dateTo,
            [FromQuery] string? searchQuery,
            [FromQuery] Guid? warehouseId,
            [FromQuery] Guid? orderId,
            [FromQuery] Guid? customerId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            Guid? finalCustomerId = null;

            if (userRole.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (!Guid.TryParse(customerIdClaim, out var parsedCustomerId))
                {
                    return Unauthorized(ApiResponse<object>.Failure("CustomerId claim is missing or invalid in the token."));
                }
                finalCustomerId = parsedCustomerId;
            }
            else
            {
                // Admin, Manager, and WarehouseOperator can optionally filter by customerId
                finalCustomerId = customerId;
            }

            var result = await _asnService.GetInboundSchedulesAsync(
                finalCustomerId,
                status,
                dateFrom,
                dateTo,
                searchQuery,
                warehouseId,
                orderId,
                pageNumber,
                pageSize);

            return Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CreateAsn([FromBody] CreateAsnRequest request)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _asnService.CreateAsnAsync(request, customerId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpGet("schedule")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSchedule([FromQuery] DateOnly? date = null, [FromQuery] string? status = null)
        {
            var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
            var result = await _asnService.GetScheduleAsync(targetDate, status);
            return Ok(result);
        }

        [HttpGet("customer/{customerId:guid}")]
        [Authorize]
        public async Task<IActionResult> GetByCustomer(Guid customerId)
        {
            var result = await _asnService.GetAsnsByCustomerIdAsync(customerId);
            return Ok(result);
        }
    }
}
