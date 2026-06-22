using System;

namespace ColdChainX.Application.DTOs
{
    public class CreateWarehouseWorkerRequest
    {
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public Guid WarehouseId { get; set; }
    }
}
