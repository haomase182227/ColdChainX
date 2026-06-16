using System;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    /// <summary>
    /// Request payload for processing inbound Quality Control (QC) check at receiving.
    /// </summary>
    public class InboundQCRequest
    {
        /// <summary>
        /// Temperature (Celsius) recorded at the time of delivery/drop-off.
        /// </summary>
        public decimal RecordedTemperature { get; set; }

        /// <summary>
        /// Name of the delivery personnel/driver dropping off the cargo.
        /// </summary>
        public string DelivererName { get; set; } = null!;

        /// <summary>
        /// Optional quality control notes or observations.
        /// </summary>
        public string? Note { get; set; }
    }
}
