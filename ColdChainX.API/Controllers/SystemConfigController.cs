using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.SystemConfigs;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/system-configs")]
    public class SystemConfigController : ControllerBase
    {
        private readonly ISystemConfigService _systemConfigService;

        public SystemConfigController(ISystemConfigService systemConfigService)
        {
            _systemConfigService = systemConfigService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllConfigs()
        {
            var result = await _systemConfigService.GetAllConfigsAsync();
            return Ok(result);
        }

        [HttpGet("{key}")]
        public async Task<IActionResult> GetConfigByKey(string key)
        {
            var result = await _systemConfigService.GetConfigByKeyAsync(key);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPut("{key}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateConfig(string key, [FromBody] UpdateSystemConfigRequest request)
        {
            var result = await _systemConfigService.UpdateConfigAsync(key, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
