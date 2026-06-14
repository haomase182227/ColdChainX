using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Attachment;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/attachments")]
    [Authorize]
    public class AttachmentController : ControllerBase
    {
        private readonly IAttachmentManagementService _attachmentService;

        public AttachmentController(IAttachmentManagementService attachmentService)
        {
            _attachmentService = attachmentService;
        }

        /// <summary>
        /// Upload a new compliance/evidence attachment.
        /// </summary>
        /// <param name="request">The attachment request containing the file and metadata.</param>
        [HttpPost]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        public async Task<IActionResult> Upload([FromForm] UploadAttachmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _attachmentService.UploadAttachmentAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Retrieve metadata of a single attachment by ID.
        /// </summary>
        /// <param name="attachmentId">The unique identifier of the attachment.</param>
        [HttpGet("{attachmentId:guid}")]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        public async Task<IActionResult> GetById([FromRoute] Guid attachmentId)
        {
            var result = await _attachmentService.GetAttachmentAsync(attachmentId);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Retrieve all attachments associated with a specific warehouse receipt.
        /// </summary>
        /// <param name="receiptId">The unique identifier of the warehouse receipt.</param>
        [HttpGet("receipts/{receiptId:guid}")]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        public async Task<IActionResult> GetByReceipt([FromRoute] Guid receiptId)
        {
            var result = await _attachmentService.GetAttachmentsByReceiptAsync(receiptId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Retrieve all attachments associated with a specific warehouse receipt item.
        /// </summary>
        /// <param name="receiptItemId">The unique identifier of the warehouse receipt item.</param>
        [HttpGet("receipt-items/{receiptItemId:guid}")]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        public async Task<IActionResult> GetByReceiptItem([FromRoute] Guid receiptItemId)
        {
            var result = await _attachmentService.GetAttachmentsByReceiptItemAsync(receiptItemId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Verify, approve or reject an attachment, and recalculate compliance if applicable.
        /// </summary>
        /// <param name="attachmentId">The unique identifier of the attachment.</param>
        /// <param name="request">The verification request containing status and reason.</param>
        [HttpPatch("{attachmentId:guid}/verify")]
        [Authorize(Roles = "Supervisor,Admin")]
        public async Task<IActionResult> Verify(
            [FromRoute] Guid attachmentId,
            [FromBody] VerifyAttachmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _attachmentService.VerifyAttachmentAsync(attachmentId, request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
