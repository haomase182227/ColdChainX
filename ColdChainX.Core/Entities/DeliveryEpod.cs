using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class DeliveryEpod
{
    public Guid EpodId { get; set; }

    public Guid? OrderId { get; set; }

    public DateTime CheckinTime { get; set; }

    public DateTime? SignedAt { get; set; }

    public string? ReceiverName { get; set; }

    public string? ReceiverPhone { get; set; }

    public string? SignImageUrl { get; set; }

    public decimal? SignLatitude { get; set; }

    public decimal? SignLongitude { get; set; }

    public int? DeliveryRating { get; set; }

    public string? Note { get; set; }

    public string? PdfUrl { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public decimal? CodAmount { get; set; }

    public decimal? CodAmountPaid { get; set; }

    public string? PaymentMethod { get; set; }

    public string? PaymentStatus { get; set; }

    public string? PaymentEvidenceImageUrl { get; set; }

    public virtual TransportOrder? Order { get; set; }

    public virtual ICollection<ReturnedItem> ReturnedItems { get; set; } = new List<ReturnedItem>();
}
