using System;
using MediatR;

namespace ColdChainX.Application.Features.Events;

public class LpnShippedEvent : INotification
{
    public Guid OrderId { get; }
    public Guid LpnId { get; }

    public LpnShippedEvent(Guid orderId, Guid lpnId)
    {
        OrderId = orderId;
        LpnId = lpnId;
    }
}
