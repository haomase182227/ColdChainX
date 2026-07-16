using ColdChainX.Application.DTOs.Chat;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IChatService
    {
        Task<ApiResponse<PagedResult<ChatMessageResponse>>> GetMessagesAsync(Guid orderId, Guid requesterId, IEnumerable<string> requesterRoles, Guid? requesterCustomerId, int pageNumber, int pageSize);

        Task<ApiResponse<ChatMessageResponse>> SendMessageAsync(Guid orderId, Guid senderId, IEnumerable<string> senderRoles, Guid? senderCustomerId, SendChatMessageRequest request);

        Task<ApiResponse<ChatParticipantResponse>> GetOrderParticipantsAsync(Guid orderId, Guid requesterId, IEnumerable<string> requesterRoles, Guid? requesterCustomerId);

        Task<ApiResponse<bool>> CanAccessOrderChatAsync(Guid orderId, Guid requesterId, IEnumerable<string> requesterRoles, Guid? requesterCustomerId);

        Task<ApiResponse<MarkChatMessagesReadResponse>> MarkMessagesAsReadAsync(Guid orderId, Guid requesterId, IEnumerable<string> requesterRoles, Guid? requesterCustomerId);

        Task<ApiResponse<ChatUnreadCountResponse>> GetUnreadCountAsync(Guid orderId, Guid requesterId, IEnumerable<string> requesterRoles, Guid? requesterCustomerId);
    }
}
