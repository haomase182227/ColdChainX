using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Mvc;
using FleetCreateDriverRequest = ColdChainX.Application.DTOs.Fleet.CreateDriverRequest;
using FleetUpdateDriverRequest = ColdChainX.Application.DTOs.Fleet.UpdateDriverRequest;
using ColdChainX.Application.DTOs.Fleet;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/drivers")]
    public class DriversController : ControllerBase
    {
        private readonly IDriverService _driverService;
        private readonly IFleetManagementService _fleetService;
        private readonly ColdChainX.Infrastructure.Persistence.ApplicationDbContext _dbContext;

        public DriversController(
            IDriverService driverService, 
            IFleetManagementService fleetService,
            ColdChainX.Infrastructure.Persistence.ApplicationDbContext dbContext)
        {
            _driverService = driverService;
            _fleetService = fleetService;
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _fleetService.GetDriversAsync();
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _fleetService.GetDriverByIdAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] FleetCreateDriverRequest request)
        {
            var result = await _fleetService.CreateDriverAsync(request);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Import([FromForm] ImportExcelRequest request)
        {
            var result = await _fleetService.ImportDriversAsync(request.ExcelFile);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPost("{driverId:guid}/licenses")]
        public async Task<IActionResult> CreateLicense(Guid driverId, [FromBody] CreateDriverLicenseRequest request)
        {
            var result = await _fleetService.CreateDriverLicenseAsync(driverId, request);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] FleetUpdateDriverRequest request)
        {
            var result = await _fleetService.UpdateDriverAsync(id, request);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success && result.Message != null && (result.Message.Contains("already") || result.Message.Contains("not found")))
                return BadRequest(result);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpGet("{id:guid}/trip-history")]
        public async Task<IActionResult> GetTripHistory(Guid id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _fleetService.GetDriverTripHistoryAsync(id, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("{driverId:guid}/vehicle-status")]
        public async Task<IActionResult> GetVehicleStatus(Guid driverId)
        {
            var activeTrip = await _dbContext.MasterTrips
                .Include(t => t.Vehicle)
                .Where(t => t.TripDrivers.Any(td => td.DriverId == driverId)
                            && (t.Status == "DISPATCHED" || t.Status == "LOADING" || t.Status == "PLANNED"))
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (activeTrip == null || activeTrip.Vehicle == null)
            {
                return Ok(new { success = true, hasActiveVehicle = false, data = (object)null });
            }

            var vehicle = activeTrip.Vehicle;
            return Ok(new
            {
                success = true,
                hasActiveVehicle = true,
                data = new
                {
                    vehicle.VehicleId,
                    vehicle.TruckPlate,
                    vehicle.VehicleType,
                    vehicle.Status,
                    vehicle.CurrentLocation,
                    TripStatus = activeTrip.Status,
                    TripId = activeTrip.TripId
                }
            });
        }

        [HttpGet("{driverId:guid}/trips")]
        public async Task<IActionResult> GetDriverTrips(Guid driverId, [FromQuery] string status = "")
        {
            var query = _dbContext.MasterTrips
                .Include(t => t.Vehicle)
                .Include(t => t.TripStops)
                    .ThenInclude(ts => ts.Location)
                .Where(t => t.TripDrivers.Any(td => td.DriverId == driverId));

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(t => t.Status == status);
            }
            else
            {
                // Default active
                var activeStatuses = new[] { "PLANNED", "PICKING", "LOADING", "LOADING_COMPLETED", "SEALED", "DISPATCHED" };
                query = query.Where(t => activeStatuses.Contains(t.Status));
            }

            var trips = await query
                .OrderByDescending(t => t.PlannedStartTime)
                .Select(t => new
                {
                    t.TripId,
                    t.Status,
                    t.PlannedStartTime,
                    t.PlannedEndTime,
                    t.TotalDistanceKm,
                    t.TargetTemperature,
                    Vehicle = t.Vehicle != null ? new 
                    { 
                        t.Vehicle.VehicleId, 
                        t.Vehicle.TruckPlate, 
                        t.Vehicle.VehicleType 
                    } : null,
                    StopCount = t.TripStops.Count,
                    Stops = t.TripStops.OrderBy(s => s.StopSequence).Select(s => new
                    {
                        s.StopSequence,
                        Address = s.Location != null ? s.Location.Address : "N/A",
                        s.PlannedArrivalTime
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = trips });
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id)
        {
            var result = await _fleetService.SoftDeleteDriverAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}
