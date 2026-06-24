using System;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    /// <summary>
    /// Request payload for processing inbound Quality Control (QC) check at receiving.
    /// All fields are submitted as a flat JSON body.
    /// </summary>
    public class InboundQCRequest
    {
        /// <summary>
        /// The unique identifier of the transport order being received.
        /// </summary>
        public Guid OrderId { get; set; }

        /// <summary>
        /// The unique identifier of the warehouse where cargo is being dropped off.
        /// </summary>
        public Guid WarehouseId { get; set; }

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
