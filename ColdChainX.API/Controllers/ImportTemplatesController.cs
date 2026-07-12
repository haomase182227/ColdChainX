using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/import-templates")]
public class ImportTemplatesController : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["vehicles"] = "vehicles_import_template.csv",
        ["drivers"] = "drivers_import_template.csv",
        ["vehicle-documents"] = "vehicle_documents_import_template.csv",
        ["driver-licenses"] = "driver_licenses_import_template.csv",
        ["weight-tiers"] = "weight_tiers_import_template.csv"
    };

    private readonly IWebHostEnvironment _environment;

    public ImportTemplatesController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet]
    public IActionResult GetAvailableTemplates()
    {
        return Ok(Templates.Select(t => new
        {
            Name = t.Key,
            FileName = t.Value,
            DownloadUrl = Url.ActionLink(nameof(Download), values: new { templateName = t.Key })
        }));
    }

    [HttpGet("{templateName}")]
    public IActionResult Download(string templateName)
    {
        if (!Templates.TryGetValue(templateName, out var fileName))
            return NotFound(new
            {
                Success = false,
                Message = "Template not found. Valid values: vehicles, drivers, vehicle-documents, driver-licenses"
            });

        var filePath = Path.Combine(_environment.ContentRootPath, "Templates", "ImportSamples", fileName);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { Success = false, Message = $"Template file is missing: {fileName}" });

        var stream = System.IO.File.OpenRead(filePath);
        return File(stream, "text/csv", fileName);
    }
}
