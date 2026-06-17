using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/driver-licenses")]
public class DriverLicensesController : ControllerBase
{
    private readonly IFleetManagementService _fleetService;

    public DriverLicensesController(IFleetManagementService fleetService)
    {
        _fleetService = fleetService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? driverId)
    {
        var result = await _fleetService.GetDriverLicensesAsync(driverId);
        return Ok(result);
    }

    [HttpGet("{licenseId:guid}")]
    public async Task<IActionResult> GetById(Guid licenseId)
    {
        var result = await _fleetService.GetDriverLicenseByIdAsync(licenseId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("{licenseId:guid}")]
    public async Task<IActionResult> Update(Guid licenseId, [FromBody] UpdateDriverLicenseRequest request)
    {
        var result = await _fleetService.UpdateDriverLicenseAsync(licenseId, request);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpDelete("{licenseId:guid}")]
    public async Task<IActionResult> Delete(Guid licenseId)
    {
        var result = await _fleetService.DeleteDriverLicenseAsync(licenseId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm] ImportExcelRequest request)
    {
        var result = await _fleetService.ImportDriverLicensesAsync(request.ExcelFile);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
