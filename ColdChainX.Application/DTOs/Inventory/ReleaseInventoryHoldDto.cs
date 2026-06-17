using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Request payload to release a held inventory record.
    /// </summary>
    public class ReleaseInventoryHoldDto
    {
        /// <summary>
        /// Explanation details of the hold resolution.
        /// </summary>
        public string ReleaseNotes { get; set; } = null!;

        /// <summary>
        /// Optional target location to relocate the stock back into (e.g. bulk storage or picking area).
        /// </summary>
        public Guid? TargetReleaseLocationId { get; set; }
    }
}
