using System.Security.Claims;
using ColdChainX.Application.DTOs.Chat;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/chat")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpGet("{orderId:guid}/messages")]
        public async Task<IActionResult> GetMessages(Guid orderId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _chatService.GetMessagesAsync(orderId, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpPost("{orderId:guid}/messages")]
        public async Task<IActionResult> SendMessage(Guid orderId, [FromBody] SendChatMessageRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var senderId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.SendMessageAsync(orderId, senderId, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
