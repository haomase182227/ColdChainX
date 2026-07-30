using System;
using System.Collections.Concurrent;
using ColdChainX.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Infrastructure.Services;

public sealed class AiAlertingControlService : IAiAlertingControlService
{
    private sealed record MuteState(DateTimeOffset Until, string Reason);
    private readonly ConcurrentDictionary<Guid, MuteState> _mutedTrips = new();
    private readonly ILogger<AiAlertingControlService> _logger;

    public AiAlertingControlService(ILogger<AiAlertingControlService> logger)
    {
        _logger = logger;
    }

    public void MuteTripAiAlerting(Guid tripId, TimeSpan duration, string reason)
    {
        var until = DateTimeOffset.UtcNow.Add(duration);
        _mutedTrips[tripId] = new MuteState(until, reason);
        _logger.LogInformation("AI alerting & monitoring muted for Trip {TripId} until {Until}. Reason: {Reason}", tripId, until, reason);
    }

    public void UnmuteTripAiAlerting(Guid tripId)
    {
        if (_mutedTrips.TryRemove(tripId, out var state))
        {
            _logger.LogInformation("AI alerting unmuted for Trip {TripId} (was muted for: {Reason})", tripId, state.Reason);
        }
    }

    public bool IsTripAiAlertingMuted(Guid tripId)
    {
        if (_mutedTrips.TryGetValue(tripId, out var state))
        {
            if (state.Until > DateTimeOffset.UtcNow)
            {
                return true;
            }
            _mutedTrips.TryRemove(tripId, out _);
        }
        return false;
    }

    public string? GetMuteReason(Guid tripId)
    {
        if (IsTripAiAlertingMuted(tripId) && _mutedTrips.TryGetValue(tripId, out var state))
        {
            return state.Reason;
        }
        return null;
    }
}
