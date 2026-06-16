namespace ColdChainX.Application.DTOs
{
    /// <summary>
    /// Request DTO for updating driver information (user profile + driver-specific fields).
    /// Used by Admin via AuthController.
    /// </summary>
    public class UpdateDriverInfoRequest
    {
        // User fields
        public string? FullName { get; set; }
        public string? NewPassword { get; set; }

        // Driver-specific fields
        public DateOnly? DateOfBirth { get; set; }
        public string? Status { get; set; }

        // Driver License fields (update existing or create new)
        public string? LicenseNumber { get; set; }
        public string? LicenseClass { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public string? DocumentUrl { get; set; }
    }
}
