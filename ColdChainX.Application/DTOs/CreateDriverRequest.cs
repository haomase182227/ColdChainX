namespace ColdChainX.Application.DTOs
{
    public class CreateDriverRequest
    {
        // User fields
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? Phone { get; set; }

        // Driver-specific fields
        public DateOnly DateOfBirth { get; set; }
        
        // Driver License information (optional during creation)
        public string? LicenseNumber { get; set; }
        public string? LicenseClass { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public string? DocumentUrl { get; set; }
    }
}
