using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Commands;

public class PickLpnCommandHandler : IRequestHandler<PickLpnCommand, PickLpnResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly IIncidentWorkflowNotificationService? _workflowNotificationService;

    public PickLpnCommandHandler(
        IApplicationDbContext context,
        IIncidentWorkflowNotificationService? workflowNotificationService = null)
    {
        _context = context;
        _workflowNotificationService = workflowNotificationService;
    }

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

        var linkedIncident = await _context.IncidentReports
            .AsNoTracking()
            .FirstOrDefaultAsync(
                incident => incident.TripId == trip.TripId && incident.Status == "REDISPATCH_PLANNED",
                cancellationToken);
        if (linkedIncident != null && _workflowNotificationService != null)
        {
            var pickedCount = await _context.Lpns.CountAsync(
                item => item.TripId == trip.TripId && item.State == LpnState.LOADING_COMPLETED,
                cancellationToken);
            var totalCount = await _context.Lpns.CountAsync(
                item => item.TripId == trip.TripId,
                cancellationToken);
            await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
            {
                IncidentId = linkedIncident.IncidentId,
                TripId = trip.TripId,
                Action = "REDISPATCH_LPN_PICKED",
                Title = "Đã lấy một LPN cho chuyến giao lại",
                Body = $"LPN {lpn.LpnCode} đã bốc xong ({pickedCount}/{totalCount}).",
                RecipientRoles = new[] { "ADMIN", "DISPATCHER", "WAREHOUSEWORKER" },
                IncludeReporter = false,
                IncludeTripDrivers = false,
                RealtimeGroups = new[] { "Group_Admin", "Group_Dispatcher", "Group_WarehouseWorker" },
                RealtimeEventName = "IncidentRedispatchLpnPicked",
                Payload = new
                {
                    linkedIncident.IncidentId,
                    TripId = trip.TripId,
                    lpn.LpnId,
                    lpn.LpnCode,
                    PickedCount = pickedCount,
                    TotalCount = totalCount,
                    LpnState = lpn.State.ToString()
                }
            }, cancellationToken);
        }

        return new PickLpnResponse
        {
            Success = true,
            Message = $"LPN {lpn.LpnCode} đã bốc xong — trạng thái LOADING_COMPLETED."
        };
    }
}
