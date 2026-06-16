using System;

namespace ColdChainX.Core.Entities
{
    public partial class OutboundOrderItem
    {
        public Guid OutboundOrderItemId { get; set; }
        public Guid OutboundOrderId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public decimal Quantity { get; set; }

        public virtual OutboundOrder OutboundOrder { get; set; } = null!;
    }
}
