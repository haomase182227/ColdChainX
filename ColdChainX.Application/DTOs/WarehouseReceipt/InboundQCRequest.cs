using System;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    public class InboundQCRequest
    {
        public Guid OrderId { get; set; }

        public Guid WarehouseId { get; set; }

        public decimal RecordedTemperature { get; set; }

        public string DelivererName { get; set; } = null!;

        public string? Note { get; set; }
    }
}
