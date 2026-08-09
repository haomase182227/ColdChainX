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

        [HttpGet("{id:guid}/utilization")]
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
