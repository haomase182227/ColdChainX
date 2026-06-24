using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Controller for retrieving warehouse-specific capacity utilization and occupancy reports.
    /// </summary>
    [ApiController]
    [Route("api/v1/warehouses")]
    [Authorize]
    public class WarehouseUtilizationController : ControllerBase
    {
        private readonly IInventoryAnalysisService _analysisService;

        public WarehouseUtilizationController(IInventoryAnalysisService analysisService)
        {
            _analysisService = analysisService;
        }

        /// <summary>
        /// Generates a warehouse occupancy and zone utilization report.
        /// </summary>
        /// <param name="id">The unique identifier of the warehouse.</param>
        [HttpGet("{id:guid}/utilization")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseUtilizationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWarehouseUtilization([FromRoute] Guid id)
        {
            var result = await _analysisService.GetWarehouseUtilizationAsync(id);
            if (!result.Success)
            {
                if (result.Message == "Warehouse not found.")
                    return NotFound(result);

                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}
