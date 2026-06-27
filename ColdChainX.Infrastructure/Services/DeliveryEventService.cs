using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ColdChainX.Infrastructure.Services;

/// <summary>
/// Implements IDeliveryEventService using SignalR NotificationHub.
/// Sends real-time notifications to relevant role groups.
/// </summary>
public class DeliveryEventService : IDeliveryEventService
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public DeliveryEventService(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public async Task NotifyHandoverPartialReturnAsync(
        Guid orderId,
        string trackingCode,
        Guid epodId,
        int rejectedLpnCount,
        int totalLpnCount,
        string orderStatus,
        string handoverPdfUrl,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Groups("Group_Dispatcher", "Group_WarehouseMonitor", "Group_Admin")
            .SendAsync("HandoverPartialReturn", new
            {
                OrderId = orderId,
                TrackingCode = trackingCode,
                EpodId = epodId,
                RejectedLpnCount = rejectedLpnCount,
                TotalLpnCount = totalLpnCount,
                OrderStatus = orderStatus,
                HandoverPdfUrl = handoverPdfUrl,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task NotifyCodPaymentConfirmedAsync(
        Guid orderId,
        string trackingCode,
        Guid epodId,
        decimal amountPaid,
        string paymentMethod,
        string orderStatus,
        string epodPdfUrl,
        string? receiverName,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Groups("Group_Admin", "Group_Sales", "Group_Dispatcher")
            .SendAsync("CodPaymentConfirmed", new
            {
                OrderId = orderId,
                TrackingCode = trackingCode,
                EpodId = epodId,
                AmountPaid = amountPaid,
                PaymentMethod = paymentMethod,
                OrderStatus = orderStatus,
                EpodPdfUrl = epodPdfUrl,
                ReceiverName = receiverName,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task NotifyTripCompletedAsync(
        Guid tripId,
        string tripCode,
        DateTime completedAt,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Groups("Group_Admin", "Group_Dispatcher", "Group_Driver")
            .SendAsync("TripCompleted", new
            {
                TripId = tripId,
                TripCode = tripCode,
                CompletedAt = completedAt,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);
    }
}
