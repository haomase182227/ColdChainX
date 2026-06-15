using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class ReleaseInventoryHoldDto
    {
        public string ReleaseNotes { get; set; } = null!;
        public Guid? TargetReleaseLocationId { get; set; } // Optional: relocate back to pick/bulk locations
    }
}
