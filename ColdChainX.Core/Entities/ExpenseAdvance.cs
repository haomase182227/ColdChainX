using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class ExpenseAdvance
{
    public Guid AdvanceId { get; set; }

    public string AdvanceCode { get; set; } = null!;

    public Guid TripId { get; set; }

    public Guid DriverId { get; set; }

    public decimal Amount { get; set; }

    public string? PaymentMethod { get; set; }

    public DateTime? AdvancedDate { get; set; }

    public Guid ApprovedBy { get; set; }

    public decimal? ClearedAmount { get; set; }

    public decimal? ReturnedAmount { get; set; }

    public string? Status { get; set; }

    public string? ClearanceStatus { get; set; }

    public string? Note { get; set; }

    public virtual User ApprovedByNavigation { get; set; } = null!;

    public virtual Driver Driver { get; set; } = null!;

    public virtual ICollection<ExpenseReceipt> ExpenseReceipts { get; set; } = new List<ExpenseReceipt>();

    public virtual MasterTrip Trip { get; set; } = null!;
}
