using System;

namespace ColdChainX.Application.DTOs
{
    public class DriverLicenseDto
    {
        public Guid LicenseId { get; set; }
        public Guid? DriverId { get; set; }
        public string LicenseNumber { get; set; } = null!;
        public string LicenseClass { get; set; } = null!;
        public DateOnly IssueDate { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public string DocumentUrl { get; set; } = null!;
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
