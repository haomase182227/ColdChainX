using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class VehicleDocument
{
    public Guid DocId { get; set; }

    public Guid? VehicleId { get; set; }

    public string DocumentNumber { get; set; } = null!;

    public string? Issuer { get; set; }

    public DateOnly IssueDate { get; set; }

    public DateOnly ExpireDate { get; set; }

    public string ImageUrl { get; set; } = null!;

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Vehicle? Vehicle { get; set; }
}
