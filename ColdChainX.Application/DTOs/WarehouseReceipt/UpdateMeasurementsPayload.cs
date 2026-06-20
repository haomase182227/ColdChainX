using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    public class UpdateMeasurementsPayload
    {
        public UpdateMeasurementsBlock WarehouseReceipt { get; set; } = null!;
    }

    public class UpdateMeasurementsBlock
    {
        public List<InboundItemMeasurement> Items { get; set; } = new List<InboundItemMeasurement>();
    }
}
