using ColdChainX.Application.DTOs.Chat;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IChatService
    {
        Task<ApiResponse<PagedResult<ChatMessageResponse>>> GetMessagesAsync(Guid orderId, int pageNumber, int pageSize);
        Task<ApiResponse<ChatMessageResponse>> SendMessageAsync(Guid orderId, Guid senderId, SendChatMessageRequest request);
    }
}
