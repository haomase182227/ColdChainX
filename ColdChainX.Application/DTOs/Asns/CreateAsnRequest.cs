namespace ColdChainX.Application.DTOs.Asns
{
    public class CreateAsnRequest
    {
        public Guid OrderId { get; set; }
        public DateTime RequestedDropoffTime { get; set; }
        public string? Phone { get; set; }
        public Guid? WarehouseId { get; set; }
        public Guid? CustomerId { get; set; }
    }
}
