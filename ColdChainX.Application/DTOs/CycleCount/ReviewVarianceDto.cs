namespace ColdChainX.Application.DTOs.CycleCount
{
    /// <summary>
    /// Request payload for a manager to review and resolve a cycle count variance discrepancy.
    /// </summary>
    public class ReviewVarianceDto
    {
        /// <summary>
        /// Flag indicating whether the counted variance is approved (true) or rejected/requires recount (false).
        /// </summary>
        public bool Approve { get; set; }

        /// <summary>
        /// Resolution notes added by the manager.
        /// </summary>
        public string? ManagerNotes { get; set; }
    }
}
