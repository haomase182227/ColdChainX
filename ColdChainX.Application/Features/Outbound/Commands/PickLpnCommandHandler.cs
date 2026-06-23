using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Commands;

public class PickLpnCommandHandler : IRequestHandler<PickLpnCommand, PickLpnResponse>
{
    private readonly IApplicationDbContext _context;

    public PickLpnCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Xác nhận một LPN đã được bốc lên xe.
    ///
    /// Precondition  : LPN.State == LOADING  (set bởi start-picking)
    /// Postcondition : LPN.State == LOADING_COMPLETED
    ///
    /// Goi tung LPN mot lan. Sau khi tat ca LPN cua chuyen deu LOADING_COMPLETED,
    /// goi POST /api/Outbound/load-trip de xac nhan toan bo chuyen.
    /// </summary>
    public async Task<PickLpnResponse> Handle(PickLpnCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
            return new PickLpnResponse { Success = false, Message = "LPN không tìm thấy." };

        if (lpn.State != LpnState.LOADING)
            return new PickLpnResponse
            {
                Success = false,
                Message = $"LPN phải ở trạng thái LOADING trước khi bốc hàng. " +
                          $"Trạng thái hiện tại: {lpn.State}. " +
                          $"Hãy gọi POST /api/Dispatch/trip/{{tripId}}/start-picking trước."
            };

        if (lpn.TripId == null)
            return new PickLpnResponse { Success = false, Message = "LPN chưa được ghép vào chuyến nào." };

        var trip = await _context.MasterTrips.FirstOrDefaultAsync(t => t.TripId == lpn.TripId.Value, cancellationToken);
        if (trip == null)
            return new PickLpnResponse { Success = false, Message = "Không tìm thấy chuyến hàng của LPN này." };

        if (trip.Status != "PICKING")
            return new PickLpnResponse
            {
                Success = false,
                Message = $"Chuyến hàng phải ở trạng thái PICKING. Trạng thái hiện tại: {trip.Status}."
            };

        lpn.State = LpnState.LOADING_COMPLETED;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new PickLpnResponse
        {
            Success = true,
            Message = $"LPN {lpn.LpnCode} đã bốc xong — trạng thái LOADING_COMPLETED."
        };
    }
}
