// Role enum removed; use string role name that maps to `Role` entity in DB

namespace ColdChainX.Application.DTOs
{
    public class RegisterRequest
    {
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        // Role name (e.g., "Admin", "Driver", "Customer").
        public string Role { get; set; } = "Customer";
    }
}
