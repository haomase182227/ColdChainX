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

    public CompleteTripLoadingCommandHandler(
        IApplicationDbContext context,
        ILogger<CompleteTripLoadingCommandHandler> logger,
        IMediator mediator,
        IPdfService pdfService)
    {
        _context = context;
        _logger = logger;
        _mediator = mediator;
        _pdfService = pdfService;
    }

    /// <summary>
    /// Xac nhan toan bo chuyen da len xe — chuyen LPN tu LOADING_COMPLETED sang RELEASED.
    ///
    /// Precondition  : TAT CA LPN cua TripId phai o trang thai LOADING_COMPLETED
    ///                 (moi LPN da duoc goi POST /api/Outbound/pick truoc do)
    /// Postcondition : LPN.State == RELEASED
    ///                 Trip.Status == LOADING_COMPLETED
    ///
    /// Sau buoc nay, goi POST /api/Dispatch/seal-and-dispatch/{tripId} de kep chi.
    /// </summary>
    public async Task<CompleteTripLoadingResponse> Handle(CompleteTripLoadingCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips.FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);
        if (trip == null)
            return new CompleteTripLoadingResponse { Success = false, Message = "Không tìm thấy chuyến hàng." };

        // Lay TAT CA LPN cua chuyen theo TripId
        var allLpns = await _context.Lpns
            .Where(l => l.TripId == request.TripId)
            .ToListAsync(cancellationToken);

        if (!allLpns.Any())
            return new CompleteTripLoadingResponse { Success = false, Message = "Chuyến hàng không có LPN nào." };

        // Kiem tra toan bo LPN da duoc boc (LOADING_COMPLETED)
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

        // Chuyen tat ca LPN sang RELEASED
        foreach (var lpn in allLpns)
        {
            lpn.State = LpnState.RELEASED;
            lpn.UpdatedAt = DateTime.UtcNow;
        }

        trip.Status = "LOADING_COMPLETED";
        await _context.SaveChangesAsync(cancellationToken);

        // Publish events cho tat ca LPN
        foreach (var lpn in allLpns)
            await _mediator.Publish(new Events.LpnShippedEvent(lpn.OrderId, lpn.LpnId), cancellationToken);

        string? manifestUrl = null;
        string? outboundTicketUrl = null;

        try
        {
            manifestUrl = await _pdfService.GenerateManifestPdfAsync(trip.TripId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể sinh Manifest PDF cho trip {TripId}.", trip.TripId);
        }

        try
        {
            outboundTicketUrl = await _pdfService.GenerateOutboundTicketPdfAsync(trip.TripId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể sinh Phiếu Xuất Kho PDF cho trip {TripId}.", trip.TripId);
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
