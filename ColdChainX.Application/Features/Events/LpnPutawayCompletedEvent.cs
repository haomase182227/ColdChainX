using System;
using MediatR;

namespace ColdChainX.Application.Features.Events;

public class LpnPutawayCompletedEvent : INotification
{
    public Guid OrderId { get; }
    public Guid LpnId { get; }

    public LpnPutawayCompletedEvent(Guid orderId, Guid lpnId)
    {
        OrderId = orderId;
        LpnId = lpnId;
    }
}
