using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Invoices;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/invoices")]
    [Authorize]
    public class InvoicesController : ControllerBase
    {
        private readonly IInvoiceService _invoiceService;

        public InvoicesController(IInvoiceService invoiceService)
        {
            _invoiceService = invoiceService;
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoices(
            [FromQuery] string? status,
            [FromQuery] Guid? customerId = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            if (pageNumber <= 0 || pageSize <= 0)
            {
                return BadRequest(ApiResponse<object>.Failure("PageNumber and PageSize must be greater than zero."));
            }

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

            var result = await _invoiceService.GetInvoicesAsync(finalCustomerId, status, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("{invoiceId:guid}")]
        public async Task<IActionResult> GetInvoiceById([FromRoute] Guid invoiceId)
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            Guid? customerId = null;

            if (userRole.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (!Guid.TryParse(customerIdClaim, out var parsedCustomerId))
                {
                    return Unauthorized(ApiResponse<object>.Failure("CustomerId claim is missing or invalid in the token."));
                }
                customerId = parsedCustomerId;
            }

            var result = await _invoiceService.GetInvoiceByIdAsync(invoiceId, customerId, userRole);
            if (!result.Success)
            {
                return StatusCode(result.StatusCode != 0 ? result.StatusCode : StatusCodes.Status404NotFound, result);
            }

            return Ok(result);
        }

        [HttpGet("~/api/v1/orders/{orderId:guid}/invoices")]
        public async Task<IActionResult> GetInvoicesByOrder([FromRoute] Guid orderId)
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            Guid? customerId = null;

            if (userRole.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (!Guid.TryParse(customerIdClaim, out var parsedCustomerId))
                {
                    return Unauthorized(ApiResponse<object>.Failure("CustomerId claim is missing or invalid in the token."));
                }
                customerId = parsedCustomerId;
            }

            var result = await _invoiceService.GetInvoicesByOrderIdAsync(orderId, customerId, userRole);
            if (!result.Success)
            {
                return NotFound(result);
            }

            return Ok(result);
        }

        [HttpPost("generate-periodic")]
        [Authorize(Roles = "Accountant,Admin,WarehouseWorker")]
        public async Task<IActionResult> GeneratePeriodicInvoices()
        {
            var result = await _invoiceService.GeneratePeriodicInvoicesAsync();
            return Ok(result);
        }
    }
}
