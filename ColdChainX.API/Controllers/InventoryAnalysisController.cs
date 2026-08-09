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

        [HttpGet("expiry-alerts")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<ExpiryAlertResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetExpiryAlerts(
            [FromQuery] Guid? warehouseId = null,
            [FromQuery] int? warningDays = null,
            [FromQuery] int? daysThreshold = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var effectiveDays = warningDays ?? daysThreshold ?? 30;
            if (effectiveDays < 0)
                return BadRequest(ApiResponse<object>.Failure("DaysThreshold must be non-negative."));

            if (pageNumber <= 0 || pageSize <= 0)
                return BadRequest(ApiResponse<object>.Failure("PageNumber and PageSize must be greater than zero."));

            var result = await _analysisService.GetExpiryAlertsAsync(warehouseId, effectiveDays, pageNumber, pageSize);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpGet("aging-report")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<AgingStockResponse>>), StatusCodes.Status200OK)]
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

        [HttpGet("temperature-audits")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<TempAuditResponse>>), StatusCodes.Status200OK)]
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
