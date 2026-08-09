namespace ColdChainX.Application.DTOs
{
    public class UpdateDriverInfoRequest
    {
        public string? FullName { get; set; }
        public string? NewPassword { get; set; }

        public DateOnly? DateOfBirth { get; set; }
        public string? Status { get; set; }

        public string? LicenseNumber { get; set; }
        public string? LicenseClass { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public string? DocumentUrl { get; set; }
    }

    public class UpdateDriverFullNameRequest
    {
        public string FullName { get; set; } = null!;
    }

    public class UpdateDriverPasswordRequest
    {
        public string NewPassword { get; set; } = null!;
    }

    public class UpdateDriverDobRequest
    {
        public DateOnly DateOfBirth { get; set; }
    }

    public class UpdateDriverStatusRequest
    {
        public string Status { get; set; } = null!;
    }

    public class UpdateDriverLicenseRequest
    {
        public string LicenseNumber { get; set; } = null!;
        public string LicenseClass { get; set; } = null!;
        public DateOnly IssueDate { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public string? DocumentUrl { get; set; }
    }
}
