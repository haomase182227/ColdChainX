using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class MaintenanceTicket
{
    public Guid TicketId { get; set; }

    public string TicketCode { get; set; } = null!;

    public Guid? VehicleId { get; set; }

    public string MaintenanceType { get; set; } = null!;

    public string GarageName { get; set; } = null!;

    public string Description { get; set; } = null!;

    public decimal? Cost { get; set; }

    public DateOnly IssueDate { get; set; }

    public DateOnly? CompletionDate { get; set; }

    public string? Status { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual Vehicle? Vehicle { get; set; }
}
