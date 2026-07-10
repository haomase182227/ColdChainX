using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.SystemConfigs
{
    public class UpdateSystemConfigRequest
    {
        [Required]
        public string Value { get; set; } = null!;

        public string? Description { get; set; }
    }
}
