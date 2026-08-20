using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Features.Outbound.Commands;

public class CompleteTripLoadingCommandHandler : IRequestHandler<CompleteTripLoadingCommand, CompleteTripLoadingResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CompleteTripLoadingCommandHandler> _logger;
    private readonly IMediator _mediator;
    private readonly IPdfService _pdfService;
    private readonly IIncidentWorkflowNotificationService? _workflowNotificationService;

    public CompleteTripLoadingCommandHandler(
        IApplicationDbContext context,
        ILogger<CompleteTripLoadingCommandHandler> logger,
        IMediator mediator,
        IPdfService pdfService,
        IIncidentWorkflowNotificationService? workflowNotificationService = null)
    {
        _context = context;
        _logger = logger;
        _mediator = mediator;
        _pdfService = pdfService;
        _workflowNotificationService = workflowNotificationService;
    }

    public async Task<CompleteTripLoadingResponse> Handle(CompleteTripLoadingCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips.FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);
        if (trip == null)
            return new CompleteTripLoadingResponse { Success = false, Message = "Không tìm thấy chuyến hàng." };

        var allLpns = await _context.Lpns
            .Where(l => l.TripId == request.TripId)
            .ToListAsync(cancellationToken);

        if (!allLpns.Any())
            return new CompleteTripLoadingResponse { Success = false, Message = "Chuyến hàng không có LPN nào." };

        var notDoneLpns = allLpns.Where(l => l.State != LpnState.LOADING_COMPLETED).ToList();
        if (notDoneLpns.Any())
        {
            var codes = string.Join(", ", notDoneLpns.Select(l => $"{l.LpnCode}({l.State})"));
            return new CompleteTripLoadingResponse
            {
                Success = false,
                Message = $"Còn {notDoneLpns.Count}/{allLpns.Count} LPN chưa ở trạng thái LOADING_COMPLETED: {codes}. " +
                          $"Vui lòng gọi POST /api/Outbound/pick cho từng LPN còn lại trước khi xác nhận chuyến."
            };
        }

        foreach (var lpn in allLpns)
        {
            lpn.State = LpnState.RELEASED;
            lpn.UpdatedAt = DateTime.UtcNow;
        }

        trip.Status = "LOADING_COMPLETED";
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var lpn in allLpns)
            await _mediator.Publish(new Events.LpnShippedEvent(lpn.OrderId, lpn.LpnId), cancellationToken);

        string? manifestUrl = null;
        string? outboundTicketUrl = null;

        try
        {
            manifestUrl = await _pdfService.GenerateManifestPdfAsync(trip.TripId);
            if (!string.IsNullOrEmpty(manifestUrl))
            {
                var adminUser = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Role != null && (u.Role.RoleName.ToUpper() == "ADMIN" || u.Role.RoleName.ToUpper() == "WAREHOUSEWORKER"));
                var currentUserId = adminUser?.UserId ?? Guid.Empty;

                _context.TransportDocuments.Add(new Core.Entities.TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    DocType = "MANIFEST",
                    ImageUrl = manifestUrl,
                    CreatedAt = DateTime.UtcNow,
                    UploadedBy = currentUserId
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể sinh Manifest PDF cho trip {TripId}.", trip.TripId);
        }

        try
        {
            outboundTicketUrl = await _pdfService.GenerateOutboundTicketPdfAsync(trip.TripId);
            if (!string.IsNullOrEmpty(outboundTicketUrl))
            {
                var adminUser = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Role != null && (u.Role.RoleName.ToUpper() == "ADMIN" || u.Role.RoleName.ToUpper() == "WAREHOUSEWORKER"));
                var currentUserId = adminUser?.UserId ?? Guid.Empty;

                _context.TransportDocuments.Add(new Core.Entities.TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    DocType = "OUTBOUND-TICKET",
                    ImageUrl = outboundTicketUrl,
                    CreatedAt = DateTime.UtcNow,
                    UploadedBy = currentUserId
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể sinh Phiếu Xuất Kho PDF cho trip {TripId}.", trip.TripId);
        }

        if (!string.IsNullOrEmpty(manifestUrl) || !string.IsNullOrEmpty(outboundTicketUrl))
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        var linkedIncident = await _context.IncidentReports
            .AsNoTracking()
            .FirstOrDefaultAsync(
                incident => incident.TripId == trip.TripId && incident.Status == "REDISPATCH_PLANNED",
                cancellationToken);
        if (linkedIncident != null && _workflowNotificationService != null)
        {
            await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
            {
                IncidentId = linkedIncident.IncidentId,
                TripId = trip.TripId,
                Action = "REDISPATCH_LOADING_COMPLETED",
                Title = "Đã xếp xong chuyến giao lại",
                Body = $"Kho đã xếp xong {allLpns.Count} LPN cho chuyến {trip.TripId}; sẵn sàng kẹp seal.",
                RecipientRoles = new[] { "ADMIN", "DISPATCHER", "WAREHOUSEWORKER" },
                IncludeReporter = false,
                IncludeTripDrivers = false,
                RealtimeGroups = new[] { "Group_Admin", "Group_Dispatcher", "Group_WarehouseWorker" },
                RealtimeEventName = "IncidentRedispatchLoadingCompleted",
                Payload = new
                {
                    linkedIncident.IncidentId,
                    TripId = trip.TripId,
                    LpnCount = allLpns.Count,
                    TripStatus = trip.Status,
                    ManifestPdfUrl = manifestUrl,
                    OutboundTicketPdfUrl = outboundTicketUrl
                }
            }, cancellationToken);
        }

        return new CompleteTripLoadingResponse
        {
            Success = true,
            Message = $"Xác nhận chuyến {trip.TripId} thành công — {allLpns.Count} LPN đã RELEASED (xuất kho).",
            ManifestPdfUrl = manifestUrl,
            HandoverPdfUrl = null,
            OutboundTicketPdfUrl = outboundTicketUrl
        };
    }
}
