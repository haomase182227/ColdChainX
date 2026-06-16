namespace ColdChainX.Application.DTOs
{
    /// <summary>
    /// Full request DTO for updating all driver information at once (used internally by service).
    /// </summary>
    public class UpdateDriverInfoRequest
    {
        // User fields
        public string? FullName { get; set; }
        public string? NewPassword { get; set; }

        // Driver-specific fields
        public DateOnly? DateOfBirth { get; set; }
        public string? Status { get; set; }

        // Driver License fields
        public string? LicenseNumber { get; set; }
        public string? LicenseClass { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public string? DocumentUrl { get; set; }
    }

    /// <summary>
    /// Update driver's display name only.
    /// </summary>
    public class UpdateDriverFullNameRequest
    {
        public string FullName { get; set; } = null!;
    }

    /// <summary>
    /// Update driver's login password only.
    /// </summary>
    public class UpdateDriverPasswordRequest
    {
        public string NewPassword { get; set; } = null!;
    }

    /// <summary>
    /// Update driver's date of birth only.
    /// </summary>
    public class UpdateDriverDobRequest
    {
        public DateOnly DateOfBirth { get; set; }
    }

    /// <summary>
    /// Update driver's operational status only.
    /// Allowed values: AVAILABLE, ON_TRIP, OFFLINE, INACTIVE
    /// </summary>
    public class UpdateDriverStatusRequest
    {
        public string Status { get; set; } = null!;
    }

    /// <summary>
    /// Update or create driver license information.
    /// </summary>
    public class UpdateDriverLicenseRequest
    {
        public string LicenseNumber { get; set; } = null!;
        public string LicenseClass { get; set; } = null!;
        public DateOnly IssueDate { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public string? DocumentUrl { get; set; }
    }
}
