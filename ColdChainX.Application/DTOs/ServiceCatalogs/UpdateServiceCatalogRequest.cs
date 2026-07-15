using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.ServiceCatalogs
{
    public class UpdateServiceCatalogRequest
    {
        [Required]
        [MaxLength(200)]
        public string ServiceName { get; set; } = null!;

        public string? Description { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal DefaultPrice { get; set; }

        public bool IsMandatory { get; set; }

        public bool IsActive { get; set; }
    }
}
