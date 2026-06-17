using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Attachment;
using ColdChainX.Application.Models;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages file attachments uploaded as supporting evidence for inbound receipts, quality logs, or compliance checks.
    /// </summary>
    [ApiController]
    [Route("api/v1/attachments")]
    [Authorize]
    public class AttachmentController : ControllerBase
    {
        private readonly IAttachmentManagementService _attachmentService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AttachmentController"/> class.
        /// </summary>
        /// <param name="attachmentService">The service used to manage attachments.</param>
        public AttachmentController(IAttachmentManagementService attachmentService)
        {
            _attachmentService = attachmentService;
        }

        /// <summary>
        /// Upload a new compliance/evidence attachment.
        /// </summary>
        /// <remarks>
        /// Uploads files (e.g. photos, PDFs, text files) and binds them polymorphically to receipts or orders.
        /// 
        /// Business purpose:
        /// Store physical proof of quality checks, temperature sensor logs, and customs clearances for audit transparency.
        /// 
        /// Required roles:
        /// WarehouseOperator, Supervisor, Admin
        /// 
        /// Workflow impact:
        /// Associates supporting documents with receipts, enabling compliance evaluation.
        /// </remarks>
        /// <param name="request">The attachment request containing the file binary and polymorphic reference IDs.</param>
        /// <returns>The details of the uploaded attachment.</returns>
        [HttpPost]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        [ProducesResponseType(typeof(ApiResponse<AttachmentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Upload([FromForm] UploadAttachmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _attachmentService.UploadAttachmentAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Retrieve metadata of a single attachment by ID.
        /// </summary>
        /// <remarks>
        /// Fetches metadata, storage location, and URLs for an attachment.
        /// 
        /// Business purpose:
        /// View the upload parameters and download links for specific compliance documents.
        /// 
        /// Required roles:
        /// WarehouseOperator, Supervisor, Admin
        /// 
        /// Workflow impact:
        /// Read-only access for viewing or displaying documents in UI viewers.
        /// </remarks>
        /// <param name="attachmentId">The unique identifier of the attachment.</param>
        /// <returns>The attachment details.</returns>
        [HttpGet("{attachmentId:guid}")]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        [ProducesResponseType(typeof(ApiResponse<AttachmentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
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
        /// <remarks>
        /// Lists all supporting document files uploaded for an inbound receipt.
        /// 
        /// Business purpose:
        /// Audit receipt evidence (e.g. deliverer sign-offs, temperature logs) prior to final receipt closure.
        /// 
        /// Required roles:
        /// WarehouseOperator, Supervisor, Admin
        /// 
        /// Workflow impact:
        /// Read-only list retrieval for quality control verification dashboards.
        /// </remarks>
        /// <param name="receiptId">The unique identifier of the warehouse receipt.</param>
        /// <returns>A list of matching attachments.</returns>
        [HttpGet("receipts/{receiptId:guid}")]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        [ProducesResponseType(typeof(ApiResponse<List<AttachmentResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
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
        /// <remarks>
        /// Lists supporting documents uploaded for a specific line item in an inbound shipment.
        /// 
        /// Business purpose:
        /// Examine phytosanitary permits or item certificates of origin at the SKU level.
        /// 
        /// Required roles:
        /// WarehouseOperator, Supervisor, Admin
        /// 
        /// Workflow impact:
        /// Provides context for compliance status evaluations at the item level.
        /// </remarks>
        /// <param name="receiptItemId">The unique identifier of the warehouse receipt item.</param>
        /// <returns>A list of matching attachments.</returns>
        [HttpGet("receipt-items/{receiptItemId:guid}")]
        [Authorize(Roles = "WarehouseOperator,Supervisor,Admin")]
        [ProducesResponseType(typeof(ApiResponse<List<AttachmentResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
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
        /// <remarks>
        /// Updates the verification status (APPROVED/REJECTED) of an attachment and triggers compliance evaluation.
        /// 
        /// Business purpose:
        /// Audit submitted documents and verify they satisfy regulatory safety rules.
        /// 
        /// Required roles:
        /// Supervisor, Admin
        /// 
        /// Workflow impact:
        /// Recalculates warehouse receipt compliance check. Rejections will flag warnings in the receipt workflow.
        /// </remarks>
        /// <param name="attachmentId">The unique identifier of the attachment.</param>
        /// <param name="request">The verification status and optional rejection reason.</param>
        /// <returns>The resulting compliance check outcomes.</returns>
        [HttpPatch("{attachmentId:guid}/verify")]
        [Authorize(Roles = "Supervisor,Admin")]
        [ProducesResponseType(typeof(ApiResponse<ComplianceCheckResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Verify(
            [FromRoute] Guid attachmentId,
            [FromBody] VerifyAttachmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _attachmentService.VerifyAttachmentAsync(attachmentId, request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
