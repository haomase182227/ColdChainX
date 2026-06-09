namespace ColdChainX.Application.DTOs
{
    public class CreateCustomerRequest
    {
        // User fields
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? Phone { get; set; }

        // Customer-specific fields
        public string CompanyName { get; set; } = null!;
        public string TaxCode { get; set; } = null!;
        public string? Address { get; set; }
        public int? PaymentTerm { get; set; }
    }
}
