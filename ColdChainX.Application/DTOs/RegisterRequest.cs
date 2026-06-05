using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs
{
    public class RegisterRequest
    {
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public Role Role { get; set; } = Role.Staff;
    }
}
