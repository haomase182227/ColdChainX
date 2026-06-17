using System.Collections.Generic;

namespace ColdChainX.Application.Models
{
    public class ComplianceCheckResult
    {
        public bool Passed { get; set; }
        public List<string> MissingRequirements { get; set; } = new();
        public List<string> FailedRequirements { get; set; } = new();
        public List<string> PendingRequirements { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
