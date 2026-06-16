namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Request payload for rejecting a pending stock adjustment.
    /// </summary>
    public class RejectAdjustmentRequest
    {
        /// <summary>
        /// Explanation details of why the adjustment was rejected.
        /// </summary>
        public string RejectionReason { get; set; } = null!;
    }
}
