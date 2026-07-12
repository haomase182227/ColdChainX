using ColdChainX.Application.DTOs.Routes;
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

        [HttpGet("{routeId}/booking-options")]
        public async Task<IActionResult> GetRouteBookingOptions(Guid routeId)
        {
            var result = await _routeService.GetRouteBookingOptionsAsync(routeId);
            if (!result.Success)
            {
                return NotFound(result);
            }
            return Ok(result);
        }

        // --- Route Schedules ---

        [HttpGet("{routeId}/schedules")]
        public async Task<IActionResult> GetRouteSchedules(Guid routeId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _routeService.GetRouteSchedulesAsync(routeId, pageIndex, pageSize);
            return Ok(result);
        }

        [HttpPost("{routeId}/schedules")]
        public async Task<IActionResult> AddRouteSchedule(Guid routeId, [FromBody] CreateRouteScheduleRequest request)
        {
            var result = await _routeService.AddRouteScheduleAsync(routeId, request);
            if (!result.Success)
                return BadRequest(result);
            return Ok(result);
        }

        [HttpDelete("{routeId}/schedules/{scheduleId}")]
        public async Task<IActionResult> DeleteRouteSchedule(Guid routeId, Guid scheduleId)
        {
            var result = await _routeService.DeleteRouteScheduleAsync(routeId, scheduleId);
            if (!result.Success)
                return NotFound(result);
            return Ok(result);
        }

        // --- Route Stops ---

        [HttpGet("{routeId}/stops")]
        public async Task<IActionResult> GetRouteStops(Guid routeId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _routeService.GetRouteStopsAsync(routeId, pageIndex, pageSize);
            return Ok(result);
        }

        [HttpPost("{routeId}/stops")]
        public async Task<IActionResult> AddRouteStop(Guid routeId, [FromBody] CreateRouteStopRequest request)
        {
            var result = await _routeService.AddRouteStopAsync(routeId, request);
            if (!result.Success)
                return BadRequest(result);
            return Ok(result);
        }

        [HttpDelete("{routeId}/stops/{stopId}")]
        public async Task<IActionResult> DeleteRouteStop(Guid routeId, Guid stopId)
        {
            var result = await _routeService.DeleteRouteStopAsync(routeId, stopId);
            if (!result.Success)
                return NotFound(result);
            return Ok(result);
        }
    }
}
