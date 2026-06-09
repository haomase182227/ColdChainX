namespace ColdChainX.Application.DTOs.Customers
{
    public class CustomerResponse
    {
        public Guid CustomerId { get; set; }
        public string CompanyName { get; set; } = null!;
        public string TaxCode { get; set; } = null!;
        public string? Address { get; set; }
        public string? Email { get; set; }
        public int? PaymentTerm { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int OrderCount { get; set; }
        public int ContractCount { get; set; }
    }
}
