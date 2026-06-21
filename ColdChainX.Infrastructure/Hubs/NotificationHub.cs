using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ColdChainX.Infrastructure.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private const string SalesGroup = "Group_Sales";
        private static readonly string[] RoleGroups = { "Admin", "Dispatcher", "Driver", "Sales", "WarehouseMonitor", "WarehouseManager", "Manager", "Loader" };

        public override async Task OnConnectedAsync()
        {
            if (IsSalesGroupMember())
                await Groups.AddToGroupAsync(Context.ConnectionId, SalesGroup);

            foreach (var group in GetRoleGroups())
                await Groups.AddToGroupAsync(Context.ConnectionId, group);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (IsSalesGroupMember())
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, SalesGroup);

            foreach (var group in GetRoleGroups())
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

            await base.OnDisconnectedAsync(exception);
        }

        private bool IsSalesGroupMember()
        {
            return Context.User?.FindAll(ClaimTypes.Role)
                .Any(role => string.Equals(role.Value, "Sales", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(role.Value, "Admin", StringComparison.OrdinalIgnoreCase)) == true;
        }

        private IEnumerable<string> GetRoleGroups()
        {
            var roles = Context.User?.FindAll(ClaimTypes.Role).Select(role => role.Value) ?? Enumerable.Empty<string>();

            foreach (var role in roles)
            {
                var matchedRole = RoleGroups.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
                if (matchedRole == null) continue;

                yield return $"Group_{matchedRole}";

                if (string.Equals(matchedRole, "Dispatcher", StringComparison.OrdinalIgnoreCase))
                    yield return "Dispatch_Team";
            }
        }
    }
}
