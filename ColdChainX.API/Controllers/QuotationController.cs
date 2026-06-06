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

        [HttpPut("{quoteId:guid}/accept")]
        public async Task<IActionResult> AcceptQuotation(Guid quoteId, [FromBody] AcceptQuotationRequest request)
        {
            var result = await _quotationService.AcceptQuotationAsync(quoteId, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
