using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.ServiceCatalogs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [Route("api/service-catalogs")]
    [ApiController]
    // [Authorize] // Tạm tắt phân quyền
    public class ServiceCatalogsController : ControllerBase
    {
        private readonly IServiceCatalogService _serviceCatalogService;

        public ServiceCatalogsController(IServiceCatalogService serviceCatalogService)
        {
            _serviceCatalogService = serviceCatalogService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _serviceCatalogService.GetAllAsync();
            return Ok(result);
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            var result = await _serviceCatalogService.GetActiveAsync();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _serviceCatalogService.GetByIdAsync(id);
            if (!result.Success)
            {
                return NotFound(result);
            }
            return Ok(result);
        }

        [HttpPost]
        // [Authorize(Roles = "Admin,Manager,Sales")] // Tạm tắt phân quyền
        public async Task<IActionResult> Create([FromBody] CreateServiceCatalogRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<ServiceCatalogDto>.Failure("Invalid request data"));
            }

            var result = await _serviceCatalogService.CreateAsync(request);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return CreatedAtAction(nameof(GetById), new { id = result.Data?.ServiceCatalogId }, result);
        }

        [HttpPut("{id}")]
        // [Authorize(Roles = "Admin,Manager,Sales")] // Tạm tắt phân quyền
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceCatalogRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<ServiceCatalogDto>.Failure("Invalid request data"));
            }

            var result = await _serviceCatalogService.UpdateAsync(id, request);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpDelete("{id}")]
        // [Authorize(Roles = "Admin,Manager")] // Tạm tắt phân quyền
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _serviceCatalogService.DeleteAsync(id);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}
