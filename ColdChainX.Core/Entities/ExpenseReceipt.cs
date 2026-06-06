using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class ExpenseReceipt
{
    public Guid ReceiptId { get; set; }

    public Guid AdvanceId { get; set; }

    public string ExpenseType { get; set; } = null!;

    public string? Description { get; set; }

    public decimal Amount { get; set; }

    public DateOnly ExpenseDate { get; set; }

    public string ImageUrl { get; set; } = null!;

    public string? Status { get; set; }

    public Guid? VerifiedBy { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public string? RejectReason { get; set; }

    public DateTime? UploadedAt { get; set; }

    public virtual ExpenseAdvance Advance { get; set; } = null!;

    public virtual User? VerifiedByNavigation { get; set; }
}
