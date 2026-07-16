using ColdChainX.Application.DTOs.Chat;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services
{
    public class ChatService : IChatService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<ChatHub> _hubContext;

        public ChatService(ApplicationDbContext db, IHubContext<ChatHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
        }

        public async Task<ApiResponse<PagedResult<ChatMessageResponse>>> GetMessagesAsync(
            Guid orderId,
            Guid requesterId,
            IEnumerable<string> requesterRoles,
            Guid? requesterCustomerId,
            int pageNumber,
            int pageSize)
        {
            var access = await ValidateOrderChatAccessAsync(orderId, requesterId, requesterRoles, requesterCustomerId, null);
            if (!access.Success)
                return ApiResponse<PagedResult<ChatMessageResponse>>.Failure(access.Message);

            var query = _db.ChatMessages
                .AsNoTracking()
                .Include(m => m.Sender).ThenInclude(u => u.Role)
                .Include(m => m.Receiver).ThenInclude(u => u.Role)
                .Where(m => m.OrderId == orderId)
                .OrderByDescending(m => m.CreatedAt);

            var totalRecords = await query.CountAsync();
            var safePageSize = Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;

            var messages = await query
                .Skip((safePageNumber - 1) * safePageSize)
                .Take(safePageSize)
                .ToListAsync();
            var data = messages.Select(ToResponse).ToList();

            return ApiResponse<PagedResult<ChatMessageResponse>>.SuccessResponse(
                PagedResult<ChatMessageResponse>.Create(data, totalRecords, safePageNumber, safePageSize),
                "Messages retrieved successfully");
        }

        public async Task<ApiResponse<ChatMessageResponse>> SendMessageAsync(
            Guid orderId,
            Guid senderId,
            IEnumerable<string> senderRoles,
            Guid? senderCustomerId,
            SendChatMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MessageContent))
                return ApiResponse<ChatMessageResponse>.Failure("MessageContent is required");
            if (request.ReceiverId == Guid.Empty)
                return ApiResponse<ChatMessageResponse>.Failure("ReceiverId is required");

            var access = await ValidateOrderChatAccessAsync(orderId, senderId, senderRoles, senderCustomerId, request.ReceiverId);
            if (!access.Success)
                return ApiResponse<ChatMessageResponse>.Failure(access.Message);

            var message = new ChatMessage
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                SenderId = senderId,
                ReceiverId = request.ReceiverId,
                MessageContent = request.MessageContent.Trim(),
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                IsRead = false
            };

            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            var response = await GetMessageResponseAsync(message.Id);
            await _hubContext.Clients.Group(ChatHub.BuildOrderGroup(orderId)).SendAsync("ReceiveMessage", response);

            return ApiResponse<ChatMessageResponse>.SuccessResponse(response, "Message sent");
        }

        public async Task<ApiResponse<ChatParticipantResponse>> GetOrderParticipantsAsync(
            Guid orderId,
            Guid requesterId,
            IEnumerable<string> requesterRoles,
            Guid? requesterCustomerId)
        {
            var access = await ValidateOrderChatAccessAsync(orderId, requesterId, requesterRoles, requesterCustomerId, null);
            if (!access.Success)
                return ApiResponse<ChatParticipantResponse>.Failure(access.Message);

            var order = await _db.TransportOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .FirstAsync(o => o.OrderId == orderId);

            var customerUserId = await FindCustomerUserIdAsync(order.Customer);

            return ApiResponse<ChatParticipantResponse>.SuccessResponse(new ChatParticipantResponse
            {
                OrderId = order.OrderId,
                CustomerId = order.CustomerId,
                CustomerUserId = customerUserId,
                CustomerName = order.Customer?.CompanyName,
                CustomerEmail = order.Customer?.Email
            }, "Chat participants retrieved successfully");
        }

        public async Task<ApiResponse<bool>> CanAccessOrderChatAsync(
            Guid orderId,
            Guid requesterId,
            IEnumerable<string> requesterRoles,
            Guid? requesterCustomerId)
        {
            var access = await ValidateOrderChatAccessAsync(orderId, requesterId, requesterRoles, requesterCustomerId, null);
            if (!access.Success)
                return ApiResponse<bool>.Failure(access.Message, access.StatusCode);

            return ApiResponse<bool>.SuccessResponse(true);
        }

        public async Task<ApiResponse<MarkChatMessagesReadResponse>> MarkMessagesAsReadAsync(
            Guid orderId,
            Guid requesterId,
            IEnumerable<string> requesterRoles,
            Guid? requesterCustomerId)
        {
            var access = await ValidateOrderChatAccessAsync(orderId, requesterId, requesterRoles, requesterCustomerId, null);
            if (!access.Success)
                return ApiResponse<MarkChatMessagesReadResponse>.Failure(access.Message, access.StatusCode);

            var unreadMessages = await _db.ChatMessages
                .Where(m => m.OrderId == orderId
                            && m.ReceiverId == requesterId
                            && !m.IsRead)
                .ToListAsync();

            foreach (var message in unreadMessages)
            {
                message.IsRead = true;
            }

            await _db.SaveChangesAsync();

            return ApiResponse<MarkChatMessagesReadResponse>.SuccessResponse(new MarkChatMessagesReadResponse
            {
                UpdatedCount = unreadMessages.Count
            }, "Messages marked as read");
        }

        public async Task<ApiResponse<ChatUnreadCountResponse>> GetUnreadCountAsync(
            Guid orderId,
            Guid requesterId,
            IEnumerable<string> requesterRoles,
            Guid? requesterCustomerId)
        {
            var access = await ValidateOrderChatAccessAsync(orderId, requesterId, requesterRoles, requesterCustomerId, null);
            if (!access.Success)
                return ApiResponse<ChatUnreadCountResponse>.Failure(access.Message, access.StatusCode);

            var unreadCount = await _db.ChatMessages
                .AsNoTracking()
                .CountAsync(m => m.OrderId == orderId
                                 && m.ReceiverId == requesterId
                                 && !m.IsRead);

            return ApiResponse<ChatUnreadCountResponse>.SuccessResponse(new ChatUnreadCountResponse
            {
                OrderId = orderId,
                UnreadCount = unreadCount
            }, "Unread count retrieved successfully");
        }

        private static ChatMessageResponse ToResponse(ChatMessage message)
            => new()
            {
                Id = message.Id,
                OrderId = message.OrderId,
                SenderId = message.SenderId,
                SenderName = message.Sender.FullName,
                SenderEmail = message.Sender.Email,
                SenderRole = message.Sender.Role?.RoleName,
                ReceiverId = message.ReceiverId,
                ReceiverName = message.Receiver.FullName,
                ReceiverEmail = message.Receiver.Email,
                ReceiverRole = message.Receiver.Role?.RoleName,
                MessageContent = message.MessageContent,
                CreatedAt = message.CreatedAt,
                IsRead = message.IsRead
            };

        private async Task<ChatMessageResponse> GetMessageResponseAsync(Guid messageId)
        {
            var message = await _db.ChatMessages
                .AsNoTracking()
                .Include(m => m.Sender).ThenInclude(u => u.Role)
                .Include(m => m.Receiver).ThenInclude(u => u.Role)
                .FirstAsync(m => m.Id == messageId);

            return ToResponse(message);
        }

        private async Task<ApiResponse<object>> ValidateOrderChatAccessAsync(
            Guid orderId,
            Guid requesterId,
            IEnumerable<string> requesterRoles,
            Guid? requesterCustomerId,
            Guid? receiverId)
        {
            var roles = requesterRoles.ToList();
            var isStaff = roles.Any(IsStaffRole);
            var isCustomer = roles.Any(r => string.Equals(r, "Customer", StringComparison.OrdinalIgnoreCase));

            var order = await _db.TransportOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return ApiResponse<object>.Failure("Order not found");

            var customerUserId = await FindCustomerUserIdAsync(order.Customer);

            if (isCustomer)
            {
                if (requesterCustomerId == null || order.CustomerId != requesterCustomerId)
                    return ApiResponse<object>.Failure("Customer can only chat in their own order");

                if (receiverId.HasValue)
                {
                    var receiverIsStaff = await IsUserInStaffRoleAsync(receiverId.Value);
                    if (!receiverIsStaff)
                        return ApiResponse<object>.Failure("Customer can only send order chat messages to Sales/Admin/Manager users");
                }

                return ApiResponse<object>.SuccessResponse(null);
            }

            if (!isStaff)
                return ApiResponse<object>.Failure("Only Customer, Sales, Admin, or Manager can use order chat");

            if (receiverId.HasValue)
            {
                if (customerUserId == null)
                    return ApiResponse<object>.Failure("Customer user for this order could not be resolved by customer email");

                if (receiverId.Value != customerUserId.Value)
                    return ApiResponse<object>.Failure("Sales/Admin/Manager can only send this order chat to the order customer");
            }

            return ApiResponse<object>.SuccessResponse(null);
        }

        private async Task<Guid?> FindCustomerUserIdAsync(Customer? customer)
        {
            if (customer?.Email == null)
                return null;

            var email = customer.Email.Trim().ToLower();
            return await _db.Users
                .AsNoTracking()
                .Where(u => u.Email != null && u.Email.ToLower() == email)
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
        }

        private async Task<bool> IsUserInStaffRoleAsync(Guid userId)
        {
            var roleName = await _db.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.UserId == userId)
                .Select(u => u.Role != null ? u.Role.RoleName : null)
                .FirstOrDefaultAsync();

            return roleName != null && IsStaffRole(roleName);
        }

        private static bool IsStaffRole(string role)
        {
            return string.Equals(role, "Sales", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);
        }
    }
}
