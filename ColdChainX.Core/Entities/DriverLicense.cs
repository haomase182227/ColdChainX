using System.ComponentModel.DataAnnotations.Schema;

namespace ColdChainX.Core.Entities;

public partial class DriverLicense
{
    public Guid LicenseId { get; set; }

    public Guid? DriverId { get; set; }

    public string LicenseNumber { get; set; } = null!;

    public string LicenseClass { get; set; } = null!;

    public DateOnly IssueDate { get; set; }

    public DateOnly ExpiryDate { get; set; }

    [NotMapped]
    public string DocumentUrl { get; set; } = string.Empty;

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Driver? Driver { get; set; }
}
