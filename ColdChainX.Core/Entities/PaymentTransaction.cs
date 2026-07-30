using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class PaymentTransaction
{
    public Guid TransactionId { get; set; }

    public string TransactionCode { get; set; } = null!;

    public Guid? OrderId { get; set; }

    public Guid? InvoiceId { get; set; }

    public Guid? ClaimId { get; set; }

    public Guid? CustomerId { get; set; }

    public string TransactionType { get; set; } = null!; // "IN" (Nhận tiền COD/PayOS) hoặc "OUT" (Chi bồi thường Kế toán)

    public decimal Amount { get; set; }

    public string PaymentMethod { get; set; } = null!; // "CASH", "BANK_TRANSFER", "PAYOS", "CREDIT"

    public string? ReferenceCode { get; set; } // Mã giao dịch ngân hàng / PayOS OrderCode

    public string? EvidenceImageUrl { get; set; } // Hình ảnh UNC / Bill chuyển khoản

    public string Status { get; set; } = null!; // "PENDING", "COMPLETED", "FAILED"

    public string? Note { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual TransportOrder? Order { get; set; }
    public virtual Invoice? Invoice { get; set; }
    public virtual Claim? Claim { get; set; }
    public virtual Customer? Customer { get; set; }
    public virtual User? CreatedByNavigation { get; set; }
}
