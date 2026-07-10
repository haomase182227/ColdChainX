using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class TransportOrder
{
    public Guid OrderId { get; set; }

    public string TrackingCode { get; set; } = null!;

    public Guid? CustomerId { get; set; }

    public string ItemName { get; set; } = null!;

    public string Category { get; set; } = null!;

    public int Quantity { get; set; }

    public string PackingType { get; set; } = null!;

    public string TempCondition { get; set; } = null!;

    public bool HasStrongOdor { get; set; }

    public bool IsStackable { get; set; } = true;

    public Guid? PickupLocation { get; set; }

    public Guid? DestLocation { get; set; }



    public string Status { get; set; } = null!;

    public Guid? MasterTripId { get; set; }

    public Guid? ScheduleId { get; set; }

    public Guid? DropoffStopId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Claim> Claims { get; set; } = new List<Claim>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<CustomerContract> CustomerContracts { get; set; } = new List<CustomerContract>();

    public virtual ICollection<DeliveryEpod> DeliveryEpods { get; set; } = new List<DeliveryEpod>();

    public virtual Location? DestLocationNavigation { get; set; }

    public virtual ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();

    public virtual MasterTrip? MasterTrip { get; set; }

    public virtual RouteSchedule? Schedule { get; set; }

    public virtual RouteStop? DropoffStop { get; set; }

    public virtual ICollection<InboundAsn> InboundAsns { get; set; } = new List<InboundAsn>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual Location? PickupLocationNavigation { get; set; }

    public virtual ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();

    public virtual ICollection<TransportDocument> TransportDocuments { get; set; } = new List<TransportDocument>();

    public virtual ICollection<WarehouseReceipt> WarehouseReceipts { get; set; } = new List<WarehouseReceipt>();

    public virtual OrderDimension? OrderDimension { get; set; }
}
