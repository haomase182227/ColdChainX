using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/putaway-suggestions")]
    [Authorize]
    public class PutawaySuggestionsController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public PutawaySuggestionsController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetPutawaySuggestions([FromQuery] Guid stockId)
        {
            if (stockId == Guid.Empty)
            {
                return BadRequest("stockId must be a valid Guid.");
            }

            var result = await _inventoryService.GetPutawaySuggestionsAsync(stockId);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpGet("receipt/{receiptId:guid}")]
        public async Task<IActionResult> GetPutawaySuggestionsByReceipt(Guid receiptId)
        {
            if (receiptId == Guid.Empty)
            {
                return BadRequest("receiptId must be a valid Guid.");
            }

            var result = await _inventoryService.GetPutawaySuggestionsByReceiptAsync(receiptId);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}
