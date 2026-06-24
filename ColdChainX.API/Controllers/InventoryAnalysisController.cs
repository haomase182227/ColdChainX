using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Controller for retrieving advanced inventory analysis, reports, and compliance audits.
    /// </summary>
    [ApiController]
    [Route("api/v1/inventory")]
    [Authorize]
    public class InventoryAnalysisController : ControllerBase
    {
        private readonly IInventoryAnalysisService _analysisService;

        public InventoryAnalysisController(IInventoryAnalysisService analysisService)
        {
            _analysisService = analysisService;
        }

        /// <summary>
        /// Retrieves items that are close to their expiration dates.
        /// </summary>
        /// <param name="warehouseId">Optional warehouse identifier to filter the results.</param>
        /// <param name="warningDays">Number of days before expiration to alert (default: 30).</param>
        /// <param name="pageNumber">Page number for pagination (default: 1).</param>
        /// <param name="pageSize">Page size for pagination (default: 10).</param>
        [HttpGet("expiry-alerts")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<ExpiryAlertResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetExpiryAlerts(
            [FromQuery] Guid? warehouseId = null,
            [FromQuery] int warningDays = 30,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _analysisService.GetExpiryAlertsAsync(warehouseId, warningDays, pageNumber, pageSize);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Retrieves items that have been in storage longer than a threshold number of days.
        /// </summary>
        /// <param name="warehouseId">Optional warehouse identifier to filter the results.</param>
        /// <param name="thresholdDays">Number of days in storage to define as aging (default: 90).</param>
        /// <param name="pageNumber">Page number for pagination (default: 1).</param>
        /// <param name="pageSize">Page size for pagination (default: 10).</param>
        [HttpGet("aging-report")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<AgingStockResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetAgingInventory(
            [FromQuery] Guid? warehouseId = null,
            [FromQuery] int thresholdDays = 90,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _analysisService.GetAgingInventoryAsync(warehouseId, thresholdDays, pageNumber, pageSize);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Identifies inventory stocks stored in zones that do not match the required product temperature ranges.
        /// </summary>
        /// <param name="warehouseId">Optional warehouse identifier to filter the results.</param>
        /// <param name="pageNumber">Page number for pagination (default: 1).</param>
        /// <param name="pageSize">Page size for pagination (default: 10).</param>
        [HttpGet("temperature-audits")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<TempAuditResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetTemperatureAudits(
            [FromQuery] Guid? warehouseId = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _analysisService.GetTemperatureAuditsAsync(warehouseId, pageNumber, pageSize);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
