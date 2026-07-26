using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FleetCreateDriverRequest = ColdChainX.Application.DTOs.Fleet.CreateDriverRequest;
using FleetUpdateDriverRequest = ColdChainX.Application.DTOs.Fleet.UpdateDriverRequest;
using ColdChainX.Application.DTOs.Fleet;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/drivers")]
    public class DriversController : ControllerBase
    {
        private readonly IDriverService _driverService;
        private readonly IFleetManagementService _fleetService;
        private readonly ColdChainX.Infrastructure.Persistence.ApplicationDbContext _dbContext;
        private readonly IGoongMapService _goongMapService;

        public DriversController(
            IDriverService driverService, 
            IFleetManagementService fleetService,
            ColdChainX.Infrastructure.Persistence.ApplicationDbContext dbContext,
            IGoongMapService goongMapService)
        {
            _driverService = driverService;
            _fleetService = fleetService;
            _dbContext = dbContext;
            _goongMapService = goongMapService;
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

        [Authorize(Roles = "Driver")]
        [HttpGet("my/trip-history")]
        public async Task<IActionResult> GetMyTripHistory([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var driverId = GetDriverId();
            if (driverId == Guid.Empty) return Unauthorized(new { success = false, message = "Driver ID not found in token." });
            var result = await _fleetService.GetDriverTripHistoryAsync(driverId, pageNumber, pageSize);
            return Ok(result);
        }

        private Guid GetDriverId()
        {
            var driverIdClaim = User.Claims.FirstOrDefault(c => c.Type == "DriverId")?.Value;
            if (Guid.TryParse(driverIdClaim, out var driverId))
                return driverId;
            return Guid.Empty;
        }

        [Authorize(Roles = "Driver")]
        [HttpGet("my/vehicle-status")]
        public async Task<IActionResult> GetVehicleStatus()
        {
            var driverId = GetDriverId();
            if (driverId == Guid.Empty) return Unauthorized(new { success = false, message = "Driver ID not found in token." });
            // Only include statuses where the driver is physically operating or preparing the vehicle
            var activeStatuses = new[] { "LOADING", "LOADING_COMPLETED", "SEALED", "DISPATCHED", "IN_TRANSIT" };
            var activeTrip = await _dbContext.MasterTrips
                .Include(t => t.Vehicle)
                .Where(t => t.TripDrivers.Any(td => td.DriverId == driverId)
                            && activeStatuses.Contains(t.Status))
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
                    vehicle.Brand,
                    vehicle.ManufactureYear,
                    vehicle.VehicleType,
                    vehicle.MaxWeight,
                    vehicle.MaxCbm,
                    vehicle.InnerLengthCm,
                    vehicle.InnerWidthCm,
                    vehicle.InnerHeightCm,
                    vehicle.MinTemp,
                    vehicle.MaxTemp,
                    vehicle.CurrentLocation,
                    vehicle.CurrentOdometer,
                    vehicle.NextMaintenanceOdometer,
                    vehicle.NextMaintenanceDate,
                    vehicle.Status
                }
            });
        }

        [Authorize(Roles = "Driver,DRIVER,Shipping,SHIPPING")]
        [HttpGet("me/trips")]
        public async Task<IActionResult> GetMyTrips([FromQuery] string status = "")
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { success = false, message = "Không tìm thấy thông tin xác thực hoặc token không hợp lệ." });

            var driver = await _dbContext.Drivers.FirstOrDefaultAsync(d => d.UserId == userId);
            if (driver == null)
                return NotFound(new { success = false, message = "Không tìm thấy hồ sơ tài xế liên kết với tài khoản này." });

            // Tái sử dụng logic lấy chuyến của GetDriverTrips
            return await GetDriverTrips(driver.DriverId, status);
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

        [Authorize(Roles = "Driver")]
        [HttpGet("my/trips")]
        public async Task<IActionResult> GetDriverMyTrips([FromQuery] string status = "")
        {
            var driverId = GetDriverId();
            if (driverId == Guid.Empty) return Unauthorized(new { success = false, message = "Driver ID not found in token." });

            var query = _dbContext.TripDrivers
                .Where(td => td.DriverId == driverId && td.Trip!.Status != "CANCELLED");

            if (!string.IsNullOrWhiteSpace(status))
            {
                var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.Trim().ToUpper())
                                     .ToList();
                query = query.Where(td => statuses.Contains(td.Trip!.Status));
            }

            var trips = await query
                .OrderByDescending(td => td.Trip!.PlannedStartTime)
                .Select(td => new
                {
                    TripId = td.TripId,
                    TripCode = "TRIP-" + td.TripId.ToString().Substring(0, 8).ToUpper(),
                    Status = td.Trip!.Status,
                    VehiclePlate = td.Trip.Vehicle != null ? td.Trip.Vehicle.TruckPlate : null,
                    RouteName = td.Trip.Route != null ? td.Trip.Route.RouteCode : null,
                    Origin = td.Trip.OriginLocation != null ? td.Trip.OriginLocation.Address : "N/A",
                    Destination = td.Trip.DestinationLocation != null ? td.Trip.DestinationLocation.Address : "N/A",
                    PlannedStartTime = td.Trip.PlannedStartTime,
                    StartedAt = td.Trip.StartedAt,
                    CompletedAt = td.Trip.CompletedAt,
                    DriverRole = td.DriverRole,
                    TotalOrders = td.Trip.TransportOrders.Count,
                    WorkHours = td.AssignedDurationHours
                })
                .ToListAsync();

            return Ok(new { success = true, data = trips });
        }

        [Authorize(Roles = "Driver")]
        [HttpGet("my/trips/{tripId:guid}/detail")]
        public async Task<IActionResult> GetDriverTripDetails(Guid tripId)
        {
            var driverId = GetDriverId();
            if (driverId == Guid.Empty) return Unauthorized(new { success = false, message = "Driver ID not found in token." });

            var trip = await _dbContext.MasterTrips
                .Include(t => t.Vehicle)
                .Include(t => t.TripStops)
                    .ThenInclude(ts => ts.Location)
                .Where(t => t.TripId == tripId && t.TripDrivers.Any(td => td.DriverId == driverId))
                .Select(t => new
                {
                    t.TripId,
                    t.Status,
                    t.PlannedStartTime,
                    t.PlannedEndTime,
                    t.StartedAt,
                    t.CompletedAt,
                    t.TotalDistanceKm,
                    t.EstimatedDurationHours,
                    t.TargetTemperature,
                    Vehicle = t.Vehicle != null ? new 
                    { 
                        t.Vehicle.VehicleId, 
                        t.Vehicle.TruckPlate, 
                        t.Vehicle.VehicleType,
                        t.Vehicle.MaxWeight,
                        t.Vehicle.MaxCbm
                    } : null,
                    StopCount = t.TripStops.Count,
                    Stops = t.TripStops.OrderBy(s => s.StopSequence).Select(s => new
                    {
                        s.StopSequence,
                        Address = s.Location != null ? s.Location.Address : "N/A",
                        s.PlannedArrivalTime,
                        s.PlannedDepartureTime,
                        s.Status,
                        s.StopType,
                        Location = s.Location
                    }).ToList(),
                    OriginLocation = t.OriginLocation,
                    DestinationLocation = t.DestinationLocation
                })
                .FirstOrDefaultAsync();

            if (trip == null)
            {
                return NotFound(new { success = false, message = "Trip not found or not assigned to driver." });
            }

            string? encodedPolyline = null;
            try
            {
                if (trip.OriginLocation != null && trip.DestinationLocation != null)
                {
                    var origin = ColdChainX.Infrastructure.Services.GoongMapService.FormatCoordinate(trip.OriginLocation.Latitude, trip.OriginLocation.Longitude);
                    var destination = ColdChainX.Infrastructure.Services.GoongMapService.FormatCoordinate(trip.DestinationLocation.Latitude, trip.DestinationLocation.Longitude);
                    var waypoints = string.Join("|", trip.Stops.Where(s => s.Location != null).Select(s => 
                        ColdChainX.Infrastructure.Services.GoongMapService.FormatCoordinate(s.Location.Latitude, s.Location.Longitude)));

                    var optimizedRoute = await _goongMapService.GetOptimizedRouteAsync(
                        origin,
                        destination,
                        string.IsNullOrWhiteSpace(waypoints) ? null : waypoints);
                    
                    encodedPolyline = optimizedRoute.OverviewPolyline;
                }
            }
            catch { /* Ignore */ }

            return Ok(new 
            { 
                success = true, 
                data = new 
                { 
                    trip.TripId,
                    trip.Status,
                    trip.PlannedStartTime,
                    trip.PlannedEndTime,
                    trip.StartedAt,
                    trip.CompletedAt,
                    trip.TotalDistanceKm,
                    trip.EstimatedDurationHours,
                    trip.TargetTemperature,
                    EncodedPolyline = encodedPolyline,
                    trip.Vehicle,
                    trip.StopCount,
                    Stops = trip.Stops.Select(s => new 
                    { 
                        s.StopSequence, 
                        s.Address, 
                        s.PlannedArrivalTime, 
                        s.PlannedDepartureTime, 
                        s.Status, 
                        s.StopType 
                    })
                }
            });
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id)
        {
            var result = await _fleetService.SoftDeleteDriverAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}
