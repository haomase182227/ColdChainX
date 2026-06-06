using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs
{
    public class CreateUserRequest
    {
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? PhoneNumber { get; set; }
        public string Role { get; set; } = null!;
        public UserStatus Status { get; set; }
    }
}
