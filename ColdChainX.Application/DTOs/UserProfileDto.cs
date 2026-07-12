using System;

namespace ColdChainX.Application.DTOs
{
    public class UserProfileDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? Email { get; set; }
        public string? Role { get; set; }
        public string? Status { get; set; }
        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
