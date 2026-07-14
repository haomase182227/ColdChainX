using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Routes;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/weight-tiers")]
    public class WeightTierController : ControllerBase
    {
        private readonly IWeightTierService _weightTierService;

        public WeightTierController(IWeightTierService weightTierService)
        {
            _weightTierService = weightTierService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllWeightTiers([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _weightTierService.GetAllAsync(pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetWeightTierById(Guid id)
        {
            var result = await _weightTierService.GetByIdAsync(id);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("/api/routes/{routeId:guid}/weight-tiers")]
        public async Task<IActionResult> GetWeightTiersByRoute(Guid routeId)
        {
            var result = await _weightTierService.GetByRouteIdAsync(routeId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateWeightTier([FromBody] CreateUpdateWeightTierRequest request)
        {
            var result = await _weightTierService.CreateAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("import")]
        public async Task<IActionResult> ImportWeightTiers(Microsoft.AspNetCore.Http.IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            if (System.IO.Path.GetExtension(file.FileName).ToLower() != ".csv")
                return BadRequest("Only CSV files are allowed");

            using var stream = file.OpenReadStream();
            var result = await _weightTierService.ImportFromCsvAsync(stream);
            
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateWeightTier(Guid id, [FromBody] CreateUpdateWeightTierRequest request)
        {
            var result = await _weightTierService.UpdateAsync(id, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteWeightTier(Guid id)
        {
            var result = await _weightTierService.DeleteAsync(id);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
