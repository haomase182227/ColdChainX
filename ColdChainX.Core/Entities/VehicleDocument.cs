using System.ComponentModel.DataAnnotations.Schema;

namespace ColdChainX.Core.Entities;

public partial class VehicleDocument
{
    public Guid DocId { get; set; }

    public Guid? VehicleId { get; set; }

    public string DocumentType { get; set; } = null!;

    public string DocumentNumber { get; set; } = null!;

    public string? Issuer { get; set; }

    public DateOnly IssueDate { get; set; }

    public DateOnly? ExpireDate { get; set; }

    [NotMapped]
    public string ImageUrl { get; set; } = string.Empty;

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Vehicle? Vehicle { get; set; }
}
