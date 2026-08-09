using ColdChainX.Application.DTOs.Asns;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authorization;
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

        [HttpGet]
        [Authorize]
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
