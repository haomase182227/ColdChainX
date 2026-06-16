using System;

namespace ColdChainX.Core.Entities;

public partial class ChatMessage
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid SenderId { get; set; }

    public Guid ReceiverId { get; set; }

    public string MessageContent { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public bool IsRead { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual User Sender { get; set; } = null!;

    public virtual User Receiver { get; set; } = null!;
}
