using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Quotations;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/quotations")]
    public class QuotationController : ControllerBase
    {
        private readonly IQuotationService _quotationService;

        public QuotationController(IQuotationService quotationService)
        {
            _quotationService = quotationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetQuotations([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _quotationService.GetQuotationsAsync(pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("{quoteId:guid}")]
        public async Task<IActionResult> GetQuotationById(Guid quoteId)
        {
            var result = await _quotationService.GetQuotationByIdAsync(quoteId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("/api/orders/{orderId:guid}/quotations")]
        public async Task<IActionResult> GetQuotationsByOrder(
            Guid orderId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _quotationService.GetQuotationsByOrderAsync(orderId, pageNumber, pageSize);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("/api/customers/{customerId:guid}/quotations")]
        public async Task<IActionResult> GetQuotationsByCustomer(
            Guid customerId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _quotationService.GetQuotationsByCustomerAsync(customerId, pageNumber, pageSize);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
        public async Task<IActionResult> CreateQuotation([FromBody] CreateQuotationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _quotationService.CreateQuotationAsync(request, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{quoteId:guid}")]
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
        public async Task<IActionResult> EditQuotation(Guid quoteId, [FromBody] EditQuotationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _quotationService.EditQuotationAsync(quoteId, request, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{quoteId:guid}/send")]
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
        public async Task<IActionResult> SendQuotation(Guid quoteId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _quotationService.SendQuotationAsync(quoteId, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{quoteId:guid}/accept")]
        [Authorize]
        public async Task<IActionResult> AcceptQuotation(Guid quoteId, [FromBody] AcceptQuotationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var customerUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _quotationService.AcceptQuotationAsync(quoteId, request, customerUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
