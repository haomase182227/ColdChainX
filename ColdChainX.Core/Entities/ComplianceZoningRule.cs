using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities;

public class ComplianceZoningRule
{
    public Guid RuleId { get; set; }
    public ProductCategory ProductCategory { get; set; }
    public AttachmentSubCategory SubCategory { get; set; }
    public RequirementLevel RequirementLevel { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
