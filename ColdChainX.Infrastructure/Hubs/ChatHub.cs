using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ColdChainX.Infrastructure.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        public Task JoinOrder(Guid orderId)
            => Groups.AddToGroupAsync(Context.ConnectionId, BuildOrderGroup(orderId));

        public Task LeaveOrder(Guid orderId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildOrderGroup(orderId));

        public async Task SendMessage(Guid orderId, string message)
        {
            await Clients.Group(BuildOrderGroup(orderId)).SendAsync("ReceiveMessage", new
            {
                OrderId = orderId,
                MessageContent = message,
                SentAt = DateTime.UtcNow
            });
        }

        public static string BuildOrderGroup(Guid orderId) => $"order:{orderId}";
    }
}
