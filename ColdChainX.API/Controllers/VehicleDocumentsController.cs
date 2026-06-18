using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/vehicle-documents")]
public class VehicleDocumentsController : ControllerBase
{
    private readonly IFleetManagementService _fleetService;

    public VehicleDocumentsController(IFleetManagementService fleetService)
    {
        _fleetService = fleetService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? vehicleId)
    {
        var result = await _fleetService.GetVehicleDocumentsAsync(vehicleId);
        return Ok(result);
    }

    [HttpGet("{docId:guid}")]
    public async Task<IActionResult> GetById(Guid docId)
    {
        var result = await _fleetService.GetVehicleDocumentByIdAsync(docId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("{docId:guid}")]
    public async Task<IActionResult> Update(Guid docId, [FromBody] UpdateVehicleDocumentRequest request)
    {
        var result = await _fleetService.UpdateVehicleDocumentAsync(docId, request);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpDelete("{docId:guid}")]
    public async Task<IActionResult> Delete(Guid docId)
    {
        var result = await _fleetService.DeleteVehicleDocumentAsync(docId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm] ImportExcelRequest request)
    {
        var result = await _fleetService.ImportVehicleDocumentsAsync(request.ExcelFile);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
