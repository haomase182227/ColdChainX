import sys
import re

file_path = r'ColdChainX.API\Controllers\DispatchController.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    text = f.read()

search_vehicles = """    public async Task<IActionResult> LookupVehicles()
    {
        var result = await _vehicleService.GetAllAsync();"""

replacement_vehicles = """    public async Task<IActionResult> LookupVehicles()
    {
        var result = await _vehicleService.GetAllAsync();"""

search_drivers = """    public async Task<IActionResult> LookupDrivers()
    {
        var candidates = await _db.Drivers"""

new_endpoints = """
    [HttpGet("lookup/vehicles/by-warehouse/{warehouseId}")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupVehiclesByWarehouse(Guid warehouseId)
    {
        var result = await _vehicleService.GetAllAsync();
        var items = result.Data?
            .Where(v => v.Status == "ACTIVE" && (v.CurrentLocation == warehouseId.ToString() || v.CurrentLocation == null)) // Allow null as they might not have location set yet
            .Select(v => new
            {
                v.VehicleId,
                Label = $"{v.TruckPlate} - {v.VehicleType} | tải {v.MaxWeight}kg / {v.MaxCbm}m3",
                v.TruckPlate,
                v.VehicleType,
                v.MaxWeight,
                v.MaxCbm,
                v.InnerLengthCm,
                v.InnerWidthCm,
                v.InnerHeightCm,
                UsableCbm = v.InnerLengthCm.HasValue && v.InnerWidthCm.HasValue && v.InnerHeightCm.HasValue
                    && v.InnerLengthCm.Value > 0 && v.InnerWidthCm.Value > 0 && v.InnerHeightCm.Value > 0
                        ? Math.Min(
                            v.MaxCbm,
                            v.InnerLengthCm.Value * v.InnerWidthCm.Value * v.InnerHeightCm.Value / 1_000_000m) * 0.8m
                        : v.MaxCbm * 0.8m
            })
            .ToList();
        return Ok(items);
    }

    [HttpGet("lookup/drivers/by-warehouse/{warehouseId}")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> LookupDriversByWarehouse(Guid warehouseId)
    {
        var candidates = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Where(d => d.Status != "Offline" && d.Status != "Inactive" && d.Status != "DELETED" && d.WarehouseId == warehouseId)
            .ToListAsync();

        foreach (var d in candidates)
            await _driverAvailability.ReconcileStatusAsync(d);
        await _db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = candidates
            .Where(d => d.Status != "RELAX")
            .Select(d =>
            {
                var lic = d.DriverLicenses
                    .Where(l => l.ExpiryDate >= today && (l.Status == null || l.Status == "ACTIVE"))
                    .OrderByDescending(l => l.ExpiryDate)
                    .FirstOrDefault();

                var isLicValid = lic != null;
                var tag = !isLicValid ? "[HẾT HẠN BẰNG]" : d.Status == "ACTIVE" ? "" : $"[{d.Status}]";
                return new
                {
                    d.DriverId,
                    d.FullName,
                    d.Phone,
                    d.Status,
                    LicenseClass = lic?.LicenseClass ?? "N/A",
                    LicenseExpiryDate = lic?.ExpiryDate,
                    IsLicenseValid = isLicValid,
                    Label = $"{d.FullName} ({d.Phone}) {tag} - Hạng {lic?.LicenseClass ?? "N/A"}"
                };
            })
            .ToList();

        return Ok(items);
    }
"""

if "LookupVehiclesByWarehouse" not in text:
    text = text.replace(search_vehicles, new_endpoints + "\n" + search_vehicles)
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(text)
    print("Added the 2 new endpoints.")
else:
    print("Endpoints already exist.")