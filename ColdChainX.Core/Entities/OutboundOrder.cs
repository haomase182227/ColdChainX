using System;
using System.Collections.Generic;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities
{
    public partial class OutboundOrder
    {
        public Guid OutboundOrderId { get; set; }
        public string OrderCode { get; set; } = null!;
        public Guid CustomerId { get; set; }
        public string ReceiverName { get; set; } = null!;
        public string ReceiverPhone { get; set; } = null!;
        public string DestinationAddress { get; set; } = null!;
        public OutboundOrderStatus Status { get; set; }
        public Guid? AssignedPickerId { get; set; }
        public DateTime? AllocatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public virtual Customer Customer { get; set; } = null!;
        public virtual User? AssignedPicker { get; set; }
        public virtual ICollection<OutboundOrderItem> OutboundOrderItems { get; set; } = new List<OutboundOrderItem>();
    }
}
