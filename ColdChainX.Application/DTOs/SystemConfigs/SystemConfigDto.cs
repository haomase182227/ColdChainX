using System;

namespace ColdChainX.Application.DTOs.SystemConfigs
{
    public class SystemConfigDto
    {
        public Guid Id { get; set; }
        public string Key { get; set; } = null!;
        public string Value { get; set; } = null!;
        public string? Description { get; set; }
    }
}
