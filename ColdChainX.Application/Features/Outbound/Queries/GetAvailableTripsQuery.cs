using ColdChainX.Application.Features.Outbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Queries;

public class GetAvailableTripsQuery : IRequest<List<AvailableTripDto>>
{
    public Guid? TripId { get; set; }

    public GetAvailableTripsQuery(Guid? tripId = null)
    {
        TripId = tripId;
    }
}

public class GetAvailableTripsQueryHandler : IRequestHandler<GetAvailableTripsQuery, List<AvailableTripDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAvailableTripsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<AvailableTripDto>> Handle(GetAvailableTripsQuery request, CancellationToken cancellationToken)
    {
        var inProgressStates = new[] { LpnState.LOADING, LpnState.LOADING_COMPLETED };

        var tripsQuery = _context.MasterTrips
            .Where(t => t.Status == "PICKING");

        if (request.TripId.HasValue)
            tripsQuery = tripsQuery.Where(t => t.TripId == request.TripId.Value);

        var trips = await tripsQuery
            .Select(t => new AvailableTripDto
            {
                TripId = t.TripId,
                Status = t.Status,
                Vehicle = t.Vehicle != null ? t.Vehicle.TruckPlate : null,
                Driver = t.TripDrivers.Count > 0
                    ? string.Join(", ", t.TripDrivers.Select(td => td.Driver.FullName))
                    : null,
                PlannedStartTime = t.PlannedStartTime,
                PlannedEndTime = t.PlannedEndTime,
                EstimatedDurationHours = t.EstimatedDurationHours,
                Lpns = _context.Lpns
                    .Where(l => l.TripId == t.TripId && inProgressStates.Contains(l.State))
                    .OrderBy(l => l.LpnCode)
                    .Select(l => new AvailableTripLpnDto
                    {
                        LpnId = l.LpnId,
                        LpnCode = l.LpnCode,
                        OrderId = l.OrderId,
                        OrderCode = l.Order.TrackingCode,
                        ItemName = l.Order.ItemName,
                        Quantity = l.Quantity,
                        State = l.State.ToString()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        foreach (var trip in trips)
        {
            trip.TotalLpns = trip.Lpns.Count;
            trip.LoadingCompletedLpns = trip.Lpns.Count(l => l.State == LpnState.LOADING_COMPLETED.ToString());
            trip.ReadyToLoad = trip.TotalLpns > 0 && trip.LoadingCompletedLpns == trip.TotalLpns;
        }

        return trips.Where(t => t.TotalLpns > 0).ToList();
    }
}
