using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ColdChainX.Infrastructure.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private const string SalesGroup = "Group_Sales";

        public override async Task OnConnectedAsync()
        {
            if (IsSalesGroupMember())
                await Groups.AddToGroupAsync(Context.ConnectionId, SalesGroup);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (IsSalesGroupMember())
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, SalesGroup);

            await base.OnDisconnectedAsync(exception);
        }

        private bool IsSalesGroupMember()
        {
            return Context.User?.FindAll(ClaimTypes.Role)
                .Any(role => string.Equals(role.Value, "Sales", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(role.Value, "Admin", StringComparison.OrdinalIgnoreCase)) == true;
        }
    }
}
