namespace ColdChainX.Application.DTOs.GoogleAuth
{
    public class GoogleLoginResponse
    {
        public string Token { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public GoogleLoginUserDto User { get; set; } = null!;
    }

    public class GoogleLoginUserDto
    {
        public Guid UserId { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? DriverId { get; set; }
        public string Username { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? Email { get; set; }
        public string? Role { get; set; }
        public string? Status { get; set; }
        public string? AvatarUrl { get; set; }
        public string? AuthProvider { get; set; }
    }
}
