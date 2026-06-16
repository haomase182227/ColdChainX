namespace ColdChainX.Application.DTOs.Asns
{
    public class CreateAsnRequest
    {
        public Guid OrderId { get; set; }
        public DateTime RequestedDropoffTime { get; set; }
    }
}
