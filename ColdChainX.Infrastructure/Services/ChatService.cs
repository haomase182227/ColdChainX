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

        public async Task<ApiResponse<PagedResult<ChatMessageResponse>>> GetMessagesAsync(Guid orderId, int pageNumber, int pageSize)
        {
            var query = _db.ChatMessages
                .AsNoTracking()
                .Where(m => m.OrderId == orderId)
                .OrderByDescending(m => m.CreatedAt);

            var totalRecords = await query.CountAsync();
            var safePageSize = Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;

            var data = await query
                .Skip((safePageNumber - 1) * safePageSize)
                .Take(safePageSize)
                .Select(m => ToResponse(m))
                .ToListAsync();

            return ApiResponse<PagedResult<ChatMessageResponse>>.SuccessResponse(
                PagedResult<ChatMessageResponse>.Create(data, totalRecords, safePageNumber, safePageSize),
                "Messages retrieved successfully");
        }

        public async Task<ApiResponse<ChatMessageResponse>> SendMessageAsync(Guid orderId, Guid senderId, SendChatMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MessageContent))
                return ApiResponse<ChatMessageResponse>.Failure("MessageContent is required");
            if (request.ReceiverId == Guid.Empty)
                return ApiResponse<ChatMessageResponse>.Failure("ReceiverId is required");

            var orderExists = await _db.TransportOrders.AnyAsync(o => o.OrderId == orderId);
            if (!orderExists)
                return ApiResponse<ChatMessageResponse>.Failure("Order not found");

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

            var response = ToResponse(message);
            await _hubContext.Clients.Group(ChatHub.BuildOrderGroup(orderId)).SendAsync("ReceiveMessage", response);

            return ApiResponse<ChatMessageResponse>.SuccessResponse(response, "Message sent");
        }

        private static ChatMessageResponse ToResponse(ChatMessage message)
            => new()
            {
                Id = message.Id,
                OrderId = message.OrderId,
                SenderId = message.SenderId,
                ReceiverId = message.ReceiverId,
                MessageContent = message.MessageContent,
                CreatedAt = message.CreatedAt,
                IsRead = message.IsRead
            };
    }
}
