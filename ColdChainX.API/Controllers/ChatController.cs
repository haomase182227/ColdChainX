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
            var requesterId = GetUserId();
            if (requesterId == Guid.Empty)
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.GetMessagesAsync(orderId, requesterId, GetRoles(), GetCustomerId(), pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("customers")]
        [Authorize(Roles = "Sales,Admin,Manager")]
        public async Task<IActionResult> GetCustomerConversations(
            [FromQuery] string? search = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 30)
        {
            var requesterId = GetUserId();
            if (requesterId == Guid.Empty)
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.GetCustomerConversationsAsync(requesterId, search, pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("customers/{customerId:guid}/messages")]
        [Authorize(Roles = "Sales,Admin,Manager")]
        public async Task<IActionResult> GetCustomerMessages(
            Guid customerId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 30)
        {
            var requesterId = GetUserId();
            if (requesterId == Guid.Empty)
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.GetCustomerMessagesAsync(
                customerId,
                requesterId,
                GetRoles(),
                pageNumber,
                pageSize);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpGet("{orderId:guid}/participants")]
        public async Task<IActionResult> GetParticipants(Guid orderId)
        {
            var requesterId = GetUserId();
            if (requesterId == Guid.Empty)
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.GetOrderParticipantsAsync(orderId, requesterId, GetRoles(), GetCustomerId());
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPatch("{orderId:guid}/messages/read")]
        public async Task<IActionResult> MarkMessagesAsRead(Guid orderId)
        {
            var requesterId = GetUserId();
            if (requesterId == Guid.Empty)
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.MarkMessagesAsReadAsync(orderId, requesterId, GetRoles(), GetCustomerId());
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpGet("{orderId:guid}/unread-count")]
        public async Task<IActionResult> GetUnreadCount(Guid orderId)
        {
            var requesterId = GetUserId();
            if (requesterId == Guid.Empty)
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.GetUnreadCountAsync(orderId, requesterId, GetRoles(), GetCustomerId());
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{orderId:guid}/messages")]
        public async Task<IActionResult> SendMessage(Guid orderId, [FromBody] SendChatMessageRequest request)
        {
            var senderId = GetUserId();
            if (senderId == Guid.Empty)
                return Unauthorized("UserId claim is missing from token");

            var result = await _chatService.SendMessageAsync(orderId, senderId, GetRoles(), GetCustomerId(), request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        private Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
        }

        private Guid? GetCustomerId()
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
        }

        private IEnumerable<string> GetRoles()
        {
            return User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        }
    }
}
