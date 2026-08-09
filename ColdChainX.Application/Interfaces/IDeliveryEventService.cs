namespace ColdChainX.Application.Interfaces;

public interface IDeliveryEventService
{
    Task NotifyHandoverPartialReturnAsync(
        Guid orderId,
        string trackingCode,
        Guid epodId,
        int rejectedLpnCount,
        int totalLpnCount,
        string orderStatus,
        string handoverPdfUrl,
        CancellationToken cancellationToken = default);

    Task NotifyCodPaymentConfirmedAsync(
        Guid orderId,
        string trackingCode,
        Guid epodId,
        decimal amountPaid,
        string paymentMethod,
        string orderStatus,
        string epodPdfUrl,
        string? receiverName,
        CancellationToken cancellationToken = default);

    Task NotifyTripCompletedAsync(
        Guid tripId,
        string tripCode,
        DateTime completedAt,
        CancellationToken cancellationToken = default);
}
