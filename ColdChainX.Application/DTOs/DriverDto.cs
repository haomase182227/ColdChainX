using System;

namespace ColdChainX.Application.DTOs
{
    public class DriverDto
    {
        public Guid DriverId { get; set; }
        public DateOnly DateOfBirth { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}