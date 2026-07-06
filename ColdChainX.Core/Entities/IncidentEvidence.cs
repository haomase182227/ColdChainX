using System;

namespace ColdChainX.Core.Entities;

public partial class IncidentEvidence
{
    public Guid EvidenceId { get; set; }
    
    public Guid IncidentId { get; set; }
    
    public string EvidenceType { get; set; } = null!;
    
    public string FileUrl { get; set; } = null!;
    
    public virtual IncidentReport Incident { get; set; } = null!;
}
