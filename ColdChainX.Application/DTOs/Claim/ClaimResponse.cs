using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Claim
{
    public class ClaimResponse
    {
        public Guid ClaimId { get; set; }
        public string ClaimCode { get; set; } = null!;
        public Guid? OrderId { get; set; }
        public string? OrderTrackingCode { get; set; }
        public string ClaimType { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string? FaultOwner { get; set; }
        public string? Status { get; set; }
        public string? ResolutionNote { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public List<ClaimEvidenceResponse> Evidences { get; set; } = new();
    }

    public class ClaimEvidenceResponse
    {
        public Guid EvidenceId { get; set; }
        public string EvidenceType { get; set; } = null!;
        public string? ImageUrl { get; set; }
        public Guid UploadedBy { get; set; }
        public string UploadedByUsername { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }
}
