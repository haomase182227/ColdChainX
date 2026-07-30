namespace ColdChainX.Application.DTOs.GoogleAuth
{
    public class VerifiedGoogleUserDto
    {
        public string GoogleId { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
        public string? Picture { get; set; }
        public bool EmailVerified { get; set; }
    }
}
