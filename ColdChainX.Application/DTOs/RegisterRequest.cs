using UserRole = ColdChainX.Core.Enums.Role;

namespace ColdChainX.Application.DTOs
{
    public class RegisterRequest
    {
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public UserRole Role { get; set; } = UserRole.Customer;
    }
}
