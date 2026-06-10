using System;

namespace ColdChainX.Application.DTOs
{
    public class RoleDto
    {
        public Guid Id { get; set; }
        public string RoleName { get; set; } = null!;
        public string? Description { get; set; }
    }
}
