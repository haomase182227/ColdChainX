namespace ColdChainX.Application.DTOs
{
    public class RegisterRequest
    {
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        // Role name from the `Role` entity (e.g. "Customer", "Admin")
        public string Role { get; set; } = "Customer";
    }
}
