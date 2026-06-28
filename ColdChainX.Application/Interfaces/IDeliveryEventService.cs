namespace ColdChainX.Application.Interfaces;

/// <summary>
/// Abstraction for real-time delivery event notifications via SignalR.
/// Commands in Application layer call this service; the Infrastructure layer
/// implements it using IHubContext&lt;NotificationHub&gt;.
/// </summary>
public interface IDeliveryEventService
{
    /// <summary>
    /// Gửi thông báo khi có hàng trả lại sau nghiệm thu.
    /// Notify: Group_Dispatcher, Group_WarehouseMonitor, Group_Admin.
    /// </summary>
    Task NotifyHandoverPartialReturnAsync(
        Guid orderId,
        string trackingCode,
        Guid epodId,
        int rejectedLpnCount,
        int totalLpnCount,
        string orderStatus,
        string handoverPdfUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi thông báo khi thu tiền COD thành công.
    /// Notify: Group_Admin, Group_Sales, Group_Dispatcher.
    /// </summary>
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

    /// <summary>
    /// Gửi thông báo khi tài xế hoàn thành toàn bộ chuyến đi (tất cả stop đã DEPARTED).
    /// Notify: Group_Admin, Group_Dispatcher, Group_Driver.
    /// </summary>
    Task NotifyTripCompletedAsync(
        Guid tripId,
        string tripCode,
        DateTime completedAt,
        CancellationToken cancellationToken = default);
}
