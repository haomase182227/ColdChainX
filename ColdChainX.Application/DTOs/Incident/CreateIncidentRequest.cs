using System;
using ColdChainX.Core.Enums;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Incident
{
    public class CreateIncidentRequest
    {
        public Guid? TripId { get; set; }
        public IncidentType? IncidentType { get; set; }
        public IncidentSeverity? Severity { get; set; }
        public string Description { get; set; } = null!;
        public decimal? CurrentLatitude { get; set; }
        public decimal? CurrentLongitude { get; set; }
        public decimal DriverPaidAmount { get; set; }
        public bool RequiresRescue { get; set; }
    }

    /// <summary>
    /// Multipart request used by the mobile application when the driver wants
    /// to report an incident together with optional photos or receipts.
    /// </summary>
    public class CreateIncidentWithEvidenceRequest : CreateIncidentRequest
    {
        public List<IFormFile> EvidenceFiles { get; set; } = new();
    }
}
