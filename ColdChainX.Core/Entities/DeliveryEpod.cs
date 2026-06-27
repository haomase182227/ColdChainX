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

    /// <summary>Thời điểm bàn giao hàng xong và khách đã ký nhận (Bước 2).</summary>
    public DateTime? HandoverConfirmedAt { get; set; }

    /// <summary>Link PDF Biên bản giao nhận hàng (sinh ngay sau bước ký nhận).</summary>
    public string? HandoverPdfUrl { get; set; }

    /// <summary>Thời điểm thu tiền COD xong (Bước 3).</summary>
    public DateTime? PaymentConfirmedAt { get; set; }

    public virtual TransportOrder? Order { get; set; }

    public virtual ICollection<ReturnedItem> ReturnedItems { get; set; } = new List<ReturnedItem>();
}
