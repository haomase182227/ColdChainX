using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Claim
{
    public class CreateClaimRequest
    {
        public Guid? OrderId { get; set; }
        public string ClaimType { get; set; } = null!;
        public string Description { get; set; } = null!;
        public List<string> EvidenceImages { get; set; } = new();
    }
}
