using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs
{
    public class RegisterRequest
    {
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? PhoneNumber { get; set; }
        public Role Role { get; set; } = Role.Staff;
    }
}
