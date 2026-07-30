using System;

namespace ColdChainX.Application.Interfaces;

public interface IAiAlertingControlService
{
    void MuteTripAiAlerting(Guid tripId, TimeSpan duration, string reason);
    void UnmuteTripAiAlerting(Guid tripId);
    bool IsTripAiAlertingMuted(Guid tripId);
    string? GetMuteReason(Guid tripId);
}
