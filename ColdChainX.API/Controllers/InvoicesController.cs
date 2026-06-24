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
    /// <summary>
    /// Manages client and system invoices. Modeled after RESTful sub-resource patterns (similar to Shopify).
    /// </summary>
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

        /// <summary>
        /// Retrieve list of invoices, filtered by status, with paging.
        /// </summary>
        /// <remarks>
        /// For Customer role: Only returns invoices belonging to the authenticated customer.
        /// For Admin/Manager roles: Returns all invoices across customers, with optional customerId filter.
        /// </remarks>
        /// <param name="status">Optional status filter (e.g. UNPAID, PAID).</param>
        /// <param name="customerId">Optional customer filter (available only to Admin/Manager).</param>
        /// <param name="pageNumber">Page index (1-based).</param>
        /// <param name="pageSize">Size of each page.</param>
        /// <returns>A paginated list of invoices.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<InvoiceResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetInvoices(
            [FromQuery] string? status,
            [FromQuery] Guid? customerId = null,
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
                // Admin or Manager can optionally filter by customerId
                finalCustomerId = customerId;
            }

            var result = await _invoiceService.GetInvoicesAsync(finalCustomerId, status, pageNumber, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Retrieve detailed invoice metadata and line items by Invoice ID.
        /// </summary>
        /// <remarks>
        /// Validates user permissions before returning detailed invoice lines.
        /// </remarks>
        /// <param name="invoiceId">The unique identifier of the invoice.</param>
        /// <returns>The detailed invoice response.</returns>
        [HttpGet("{invoiceId:guid}")]
        [ProducesResponseType(typeof(ApiResponse<InvoiceResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
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
                return NotFound(result);
            }

            return Ok(result);
        }

        /// <summary>
        /// Retrieve all invoices associated with a specific Transport Order ID.
        /// </summary>
        /// <remarks>
        /// Modeled after Shopify's sub-resource pattern.
        /// Matches: GET /api/v1/orders/{orderId}/invoices
        /// </remarks>
        /// <param name="orderId">The unique identifier of the transport order.</param>
        /// <returns>A list of invoices linked to the order.</returns>
        [HttpGet("~/api/v1/orders/{orderId:guid}/invoices")]
        [ProducesResponseType(typeof(ApiResponse<List<InvoiceResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
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
    }
}
