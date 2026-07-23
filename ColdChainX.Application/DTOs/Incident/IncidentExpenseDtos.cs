using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Incident;

public sealed class UploadIncidentEvidenceRequest
{
    public string EvidenceType { get; set; } = "INCIDENT_ATTACHMENT";
    public List<IFormFile> Files { get; set; } = new();
}

public sealed class ApproveIncidentExpenseRequest
{
    public decimal ApprovedAmount { get; set; }
    public string? ApprovalNote { get; set; }
}

public sealed class ReimburseIncidentExpenseRequest
{
    public decimal ReimbursedAmount { get; set; }
    public string? Note { get; set; }
    public IFormFile ReceiptFile { get; set; } = null!;
}

