namespace ColdChainX.Application.DTOs
{
    public class RegisterRequest
    {
        // User fields
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? Phone { get; set; }
        
        // Role selection (Admin, Dispatcher, or Sales only)
        public string Role { get; set; } = "Dispatcher";
    }
}
