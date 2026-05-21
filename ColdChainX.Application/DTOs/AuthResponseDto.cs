using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs
{
    public class AuthResponseDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public Role Role { get; set; }
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public DateTime AccessTokenExpiresAt { get; set; }
    }
}
