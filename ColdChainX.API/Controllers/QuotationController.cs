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
        public async Task<IActionResult> GetQuotations()
        {
            var result = await _quotationService.GetQuotationsAsync();
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
        public async Task<IActionResult> GetQuotationsByOrder(Guid orderId)
        {
            var result = await _quotationService.GetQuotationsByOrderAsync(orderId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateQuotation([FromBody] CreateQuotationRequest request)
        {
            var result = await _quotationService.CreateQuotationAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{quoteId:guid}/accept")]
        [HttpPut("{quoteId:guid}/accept")]
        public async Task<IActionResult> AcceptQuotation(Guid quoteId, [FromBody] AcceptQuotationRequest request)
        {
            var result = await _quotationService.AcceptQuotationAsync(quoteId, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
