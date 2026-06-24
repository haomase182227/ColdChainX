using ColdChainX.Application.Features.Outbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Queries;

/// <summary>
/// Lay danh sach chuyen dang o trang thai PICKING kem tat ca LPN/don hang ben trong —
/// dung de chuan bi du lieu cho POST /api/Outbound/pick va POST /api/Outbound/load-trip.
/// Co the loc theo TripId (tuy chon) de xem chi tiet mot chuyen.
/// </summary>
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
        // Trang thai LPN dang trong qua trinh bock len xe cua mot chuyen PICKING.
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

        // Tinh cac chi so tong hop cho tung chuyen.
        foreach (var trip in trips)
        {
            trip.TotalLpns = trip.Lpns.Count;
            trip.LoadingCompletedLpns = trip.Lpns.Count(l => l.State == LpnState.LOADING_COMPLETED.ToString());
            trip.ReadyToLoad = trip.TotalLpns > 0 && trip.LoadingCompletedLpns == trip.TotalLpns;
        }

        // Chi tra ve chuyen co LPN dang xu ly.
        return trips.Where(t => t.TotalLpns > 0).ToList();
    }
}
