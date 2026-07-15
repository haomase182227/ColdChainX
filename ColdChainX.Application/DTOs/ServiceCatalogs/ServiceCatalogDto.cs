using System;

namespace ColdChainX.Application.DTOs.ServiceCatalogs
{
    public class ServiceCatalogDto
    {
        public Guid ServiceCatalogId { get; set; }
        public string ServiceCode { get; set; } = null!;
        public string ServiceName { get; set; } = null!;
        public string? Description { get; set; }
        public decimal DefaultPrice { get; set; }
        public bool IsMandatory { get; set; }

        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
