using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.ServiceCatalogs
{
    public class CreateServiceCatalogRequest
    {
        [Required]
        [MaxLength(50)]
        public string ServiceCode { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string ServiceName { get; set; } = null!;

        public string? Description { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal DefaultPrice { get; set; }

        public bool IsMandatory { get; set; } = false;

        public bool IsActive { get; set; } = true;
    }
}
