using System;

namespace ColdChainX.Application.DTOs
{
    public class DriverDto
    {
        public Guid DriverId { get; set; }
        public Guid? UserId { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public DateOnly DateOfBirth { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
        public List<DriverLicenseDto> DriverLicenses { get; set; } = new();
    }
}