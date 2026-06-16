using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/routes")]
    public class RoutesController : ControllerBase
    {
        private readonly IRouteService _routeService;

        public RoutesController(IRouteService routeService)
        {
            _routeService = routeService;
        }

        [HttpGet("options")]
        public async Task<IActionResult> GetRouteOptions([FromQuery] string? originCity, [FromQuery] string? destCity)
        {
            var result = await _routeService.GetRouteOptionsAsync(originCity, destCity);
            return Ok(result);
        }
    }
}
