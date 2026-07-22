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
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var devices = await _db.IotDevices
            .Select(d => new
            {
                d.DeviceId,
                d.DeviceCode,
                d.VehicleId,
                TruckPlate = d.Vehicle != null ? d.Vehicle.TruckPlate : null,
                d.BatteryLevel,
                d.Status,
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

        var exists = await _db.IotDevices.AnyAsync(d => d.DeviceCode == request.DeviceCode, cancellationToken);
        if (exists)
            return Conflict(new { Success = false, Error = $"Device with code '{request.DeviceCode}' already exists." });

        var device = new IotDevice
        {
            DeviceId = Guid.NewGuid(),
            DeviceCode = request.DeviceCode.Trim(),
            VehicleId = null,
            BatteryLevel = 100,
            Status = "AVAILABLE",
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

        _db.IotDevices.Remove(device);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { Success = true });
    }
}

public sealed class CreateIotDeviceRequest
{
    public string DeviceCode { get; set; } = string.Empty;
}

public sealed class UpdateIotDeviceRequest
{
    public string? DeviceCode { get; set; }
    public bool RemoveVehicle { get; set; }
    public string? Status { get; set; }
}
