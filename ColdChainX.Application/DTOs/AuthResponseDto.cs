using System;

namespace ColdChainX.Application.DTOs
{
    public class AuthResponseDto
    {
        public Guid UserId { get; set; }
        public Guid? CustomerId { get; set; }
        public string Username { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? Email { get; set; }
        public string? Role { get; set; }
        public string? Status { get; set; }
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public DateTime AccessTokenExpiresAt { get; set; }
    }
}
