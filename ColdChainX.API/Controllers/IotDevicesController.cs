using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.API.Controllers;

[ApiController]
[Authorize]
[Route("api/iot-devices")]
public sealed class IotDevicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public IotDevicesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (pageNumber <= 0 || pageSize <= 0)
            return BadRequest(new { Success = false, Error = "PageNumber and PageSize must be greater than zero." });

        var devices = await _db.IotDevices
            .Select(d => new
            {
                d.DeviceId,
                d.DeviceCode,
                d.VehicleId,
                TruckPlate = d.Vehicle != null ? d.Vehicle.TruckPlate : null,
                d.BatteryLevel,
                d.Status,
                d.IsOnline,
                d.LastPingTime,
                d.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { Success = true, Data = devices });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var device = await _db.IotDevices
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.DeviceId == id, cancellationToken);

        if (device == null)
            return NotFound(new { Success = false, Error = "IoT Device not found." });

        return Ok(new
        {
            Success = true,
            Data = new
            {
                device.DeviceId,
                device.DeviceCode,
                device.VehicleId,
                TruckPlate = device.Vehicle?.TruckPlate,
                device.BatteryLevel,
                device.Status,
                device.IsOnline,
                device.LastPingTime,
                device.CreatedAt
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateIotDeviceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceCode))
            return BadRequest(new { Success = false, Error = "DeviceCode is required." });

        if ((request.SamplingRate.HasValue && request.SamplingRate.Value <= 0) ||
            (request.SamplingRateSeconds.HasValue && request.SamplingRateSeconds.Value <= 0))
        {
            return BadRequest(new { Success = false, Error = "Sampling rate must be greater than zero (Missing required hardware identifiers or invalid sampling rate)." });
        }

        if (request.VehicleId.HasValue && request.VehicleId.Value != Guid.Empty)
        {
            var vehicleExists = await _db.Vehicles.AnyAsync(v => v.VehicleId == request.VehicleId.Value, cancellationToken);
            if (!vehicleExists)
                return NotFound(new { Success = false, Error = "Vehicle not found." });
        }

        var exists = await _db.IotDevices.AnyAsync(d => d.DeviceCode == request.DeviceCode, cancellationToken);
        if (exists)
            return Conflict(new { Success = false, Error = $"Device with code '{request.DeviceCode}' already exists." });

        var device = new IotDevice
        {
            DeviceId = Guid.NewGuid(),
            DeviceCode = request.DeviceCode.Trim(),
            VehicleId = request.VehicleId == Guid.Empty ? null : request.VehicleId,
            BatteryLevel = 100,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "AVAILABLE" : request.Status.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.IotDevices.Add(device);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { Success = true, Data = device.DeviceId });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIotDeviceRequest request, CancellationToken cancellationToken)
    {
        var device = await _db.IotDevices.FindAsync(new object[] { id }, cancellationToken);
        if (device == null)
            return NotFound(new { Success = false, Error = "IoT Device not found." });

        if (!string.IsNullOrWhiteSpace(request.DeviceCode) && request.DeviceCode != device.DeviceCode)
        {
            var exists = await _db.IotDevices.AnyAsync(d => d.DeviceCode == request.DeviceCode, cancellationToken);
            if (exists)
                return Conflict(new { Success = false, Error = $"Device with code '{request.DeviceCode}' already exists." });
            
            device.DeviceCode = request.DeviceCode.Trim();
        }

        if (request.RemoveVehicle)
        {
            device.VehicleId = null;
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
            device.Status = request.Status.Trim();

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { Success = true });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var device = await _db.IotDevices.FindAsync(new object[] { id }, cancellationToken);
        if (device == null)
            return NotFound(new { Success = false, Error = "IoT Device not found." });

        var logs = await _db.TelemetryLogs
            .Where(t => t.DeviceId == id)
            .ToListAsync(cancellationToken);
        if (logs.Count > 0)
        {
            _db.TelemetryLogs.RemoveRange(logs);
        }

        _db.IotDevices.Remove(device);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { Success = true });
    }
}

public sealed class CreateIotDeviceRequest
{
    public string DeviceCode { get; set; } = string.Empty;
    public string? DeviceType { get; set; }
    public Guid? VehicleId { get; set; }
    public int? SamplingRate { get; set; }
    public int? SamplingRateSeconds { get; set; }
    public string? Status { get; set; }
}

public sealed class UpdateIotDeviceRequest
{
    public string? DeviceCode { get; set; }
    public bool RemoveVehicle { get; set; }
    public string? Status { get; set; }
}
