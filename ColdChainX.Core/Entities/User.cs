using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class User
{
    public Guid UserId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string? Email { get; set; }

    public int? RoleId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Status { get; set; }

    public string? RefreshToken { get; set; }

    public DateTime? RefreshTokenExpiryTime { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<AlertLog> AlertLogs { get; set; } = new List<AlertLog>();

    public virtual ICollection<ClaimEvidence> ClaimEvidences { get; set; } = new List<ClaimEvidence>();

    public virtual ICollection<ExpenseAdvance> ExpenseAdvances { get; set; } = new List<ExpenseAdvance>();

    public virtual ICollection<ExpenseReceipt> ExpenseReceipts { get; set; } = new List<ExpenseReceipt>();

    public virtual ICollection<IncidentReport> IncidentReports { get; set; } = new List<IncidentReport>();

    public virtual ICollection<MaintenanceTicket> MaintenanceTickets { get; set; } = new List<MaintenanceTicket>();

    public virtual ICollection<Notification> NotificationSenders { get; set; } = new List<Notification>();

    public virtual ICollection<Notification> NotificationUsers { get; set; } = new List<Notification>();

    public virtual ICollection<ReturnedItem> ReturnedItems { get; set; } = new List<ReturnedItem>();

    public virtual Role? Role { get; set; }

    public virtual ICollection<TransportDocument> TransportDocumentUploadedByNavigations { get; set; } = new List<TransportDocument>();

    public virtual ICollection<TransportDocument> TransportDocumentVerifiedByNavigations { get; set; } = new List<TransportDocument>();

    public virtual ICollection<WarehouseReceipt> WarehouseReceipts { get; set; } = new List<WarehouseReceipt>();
}
