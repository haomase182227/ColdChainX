using System.Security.Claims;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ColdChainX.Infrastructure.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IChatService _chatService;

        public ChatHub(IChatService chatService)
        {
            _chatService = chatService;
        }

        public async Task JoinOrder(Guid orderId)
        {
            var requesterId = GetUserId();
            if (requesterId == Guid.Empty)
                throw new HubException("UserId claim is missing from token");

            var access = await _chatService.CanAccessOrderChatAsync(orderId, requesterId, GetRoles(), GetCustomerId());
            if (!access.Success)
                throw new HubException(access.Message ?? "You do not have permission to join this order chat");

            await Groups.AddToGroupAsync(Context.ConnectionId, BuildOrderGroup(orderId));
        }

        public Task LeaveOrder(Guid orderId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildOrderGroup(orderId));

        public Task SendMessage(Guid orderId, string message)
        {
            throw new HubException("Use POST /api/chat/{orderId}/messages to send chat messages");
        }

        public static string BuildOrderGroup(Guid orderId) => $"order:{orderId}";

        private Guid GetUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
        }

        private Guid? GetCustomerId()
        {
            var customerIdClaim = Context.User?.FindFirst("CustomerId")?.Value;
            return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
        }

        private IEnumerable<string> GetRoles()
        {
            return Context.User?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();
        }
    }
}
