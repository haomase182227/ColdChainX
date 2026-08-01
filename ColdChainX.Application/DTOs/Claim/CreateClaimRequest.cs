using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Claim
{
    public enum ClaimCategory
    {
        DAMAGE,
        QUALITY_VIOLATION,
        LOSS,
        DELAY,
        WRONG_ITEM
    }

    public class CreateClaimRequest
    {
        public Guid? OrderId { get; set; }
        public ClaimCategory ClaimType { get; set; }
        public string Description { get; set; } = null!;
        public List<IFormFile> EvidenceImages { get; set; } = new();
    }
}
