using System;

namespace ColdChainX.Application.DTOs
{
    public class RoleDto
    {
        public int Id { get; set; }
        public string RoleName { get; set; } = null!;
        public string? Description { get; set; }
    }
}
