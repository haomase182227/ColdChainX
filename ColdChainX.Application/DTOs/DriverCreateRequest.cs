namespace ColdChainX.Application.DTOs
{
    public class DriverCreateRequest
    {
        public DateOnly DateOfBirth { get; set; }
        public string? Status { get; set; }
    }
}