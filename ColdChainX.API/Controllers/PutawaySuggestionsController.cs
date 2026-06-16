using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Suggests optimal storage locations for inventory putaway operations based on temperature compliance and layout compatibility.
    /// </summary>
    [ApiController]
    [Route("api/v1/putaway-suggestions")]
    [Authorize]
    public class PutawaySuggestionsController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        /// <summary>
        /// Initializes a new instance of the <see cref="PutawaySuggestionsController"/> class.
        /// </summary>
        /// <param name="inventoryService">The service used to query putaway suggestions.</param>
        public PutawaySuggestionsController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Retrieve putaway suggestions for a specific stock record.
        /// </summary>
        /// <remarks>
        /// Analyzes warehouse layout, temperature rules, and item compatibility to recommend target storage locations.
        /// 
        /// Business purpose:
        /// Guide warehouse operators to store received items in compliant, available slots.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only calculation of suitable storage locations ranked by feasibility score.
        /// </remarks>
        /// <param name="stockId">The unique identifier of the stock record.</param>
        /// <returns>A list of suggested warehouse locations sorted by suitability score.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<List<PutawaySuggestionResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPutawaySuggestions([FromQuery] Guid stockId)
        {
            if (stockId == Guid.Empty)
            {
                return BadRequest(ApiResponse<object>.Failure("stockId must be a valid Guid."));
            }

            var result = await _inventoryService.GetPutawaySuggestionsAsync(stockId);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        /// <summary>
        /// Retrieve putaway suggestions for all stock items associated with a warehouse receipt.
        /// </summary>
        /// <remarks>
        /// Generates putaway recommendations for a complete inbound shipment receipt.
        /// 
        /// Business purpose:
        /// Support batch storage planning when a large inbound cargo drop-off is finalized.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only bulk calculations grouped by receipt items.
        /// </remarks>
        /// <param name="receiptId">The unique identifier of the warehouse receipt.</param>
        /// <returns>A list of suggestions grouped by stock items.</returns>
        [HttpGet("receipt/{receiptId:guid}")]
        [ProducesResponseType(typeof(ApiResponse<List<StockPutawaySuggestionsResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPutawaySuggestionsByReceipt(Guid receiptId)
        {
            if (receiptId == Guid.Empty)
            {
                return BadRequest(ApiResponse<object>.Failure("receiptId must be a valid Guid."));
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
