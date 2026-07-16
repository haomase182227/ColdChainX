using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Incident
{
    public class IncidentResponse
    {
        public Guid IncidentId { get; set; }
        public Guid? TripId { get; set; }
        public string? TripCode { get; set; }
        public string IncidentType { get; set; } = null!;
        public string Severity { get; set; } = null!;
        public string Description { get; set; } = null!;
        public decimal? CurrentLatitude { get; set; }
        public decimal? CurrentLongitude { get; set; }
        public decimal DriverPaidAmount { get; set; }
        public decimal? ReimbursedAmount { get; set; }
        public string? Status { get; set; }
        public Guid ReportedBy { get; set; }
        public string ReportedByUsername { get; set; } = null!;
        public DateTime? ReportedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNote { get; set; }
        public List<IncidentEvidenceResponse> Evidences { get; set; } = new();
    }

    public class IncidentEvidenceResponse
    {
        public Guid EvidenceId { get; set; }
        public string EvidenceType { get; set; } = null!;
        public string FileUrl { get; set; } = null!;
    }
}
