using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using ColdChainX.Application.DTOs.Attachment;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Models;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class AttachmentManagementService : IAttachmentManagementService
    {
        private readonly IWarehouseAttachmentRepository _attachmentRepository;
        private readonly IWarehouseReceiptRepository _receiptRepository;
        private readonly IFileService _fileService;
        private readonly IValidator<UploadAttachmentRequest> _uploadValidator;
        private readonly IValidator<VerifyAttachmentRequest> _verifyValidator;

        public AttachmentManagementService(
            IWarehouseAttachmentRepository attachmentRepository,
            IWarehouseReceiptRepository receiptRepository,
            IFileService fileService,
            IValidator<UploadAttachmentRequest> uploadValidator,
            IValidator<VerifyAttachmentRequest> verifyValidator)
        {
            _attachmentRepository = attachmentRepository;
            _receiptRepository = receiptRepository;
            _fileService = fileService;
            _uploadValidator = uploadValidator;
            _verifyValidator = verifyValidator;
        }

        public async Task<ApiResponse<AttachmentResponse>> UploadAttachmentAsync(UploadAttachmentRequest request, Guid userId)
        {
            var validationResult = await _uploadValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                return ApiResponse<AttachmentResponse>.Failure(errors);
            }

            try
            {
                string filePath = await _fileService.UploadFileAsync(request.File);

                var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
                var format = extension == ".pdf" ? AttachmentFormat.PDF : AttachmentFormat.IMAGE;

                var attachment = new WarehouseEvidenceAttachment
                {
                    AttachmentId = Guid.NewGuid(),
                    FileName = request.File.FileName,
                    FilePath = filePath,
                    FileSize = request.File.Length,
                    ContentType = request.File.ContentType,
                    Format = format,
                    Category = request.Category,
                    SubCategory = request.SubCategory,
                    Status = DocumentStatus.PENDING,

                    // Targets
                    WarehouseReceiptId = request.WarehouseReceiptId,
                    WarehouseReceiptItemId = request.WarehouseReceiptItemId,
                    InventoryAdjustmentId = request.InventoryAdjustmentId,
                    OutboundOrderId = request.OutboundOrderId,

                    // Optional metadata
                    DocumentNumber = request.DocumentNumber,
                    Issuer = request.Issuer,
                    IssueDate = request.IssueDate,
                    ExpiryDate = request.ExpiryDate,
                    CapturedValue = request.CapturedValue,
                    SealNumber = request.SealNumber,

                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId
                };

                await _attachmentRepository.AddAttachmentAsync(attachment);

                // Create Audit History record
                var audit = new AttachmentAuditHistory
                {
                    HistoryId = Guid.NewGuid(),
                    AttachmentId = attachment.AttachmentId,
                    PreviousStatus = null,
                    NewStatus = DocumentStatus.PENDING,
                    Reason = "Attachment uploaded.",
                    ChangedBy = userId,
                    ChangedAt = DateTime.UtcNow
                };

                await _attachmentRepository.AddAuditHistoryAsync(audit);
                await _attachmentRepository.SaveChangesAsync();

                var response = MapToResponse(attachment);
                return ApiResponse<AttachmentResponse>.SuccessResponse(response, "Attachment uploaded successfully.");
            }
            catch (Exception ex)
            {
                return ApiResponse<AttachmentResponse>.Failure($"Upload failed: {ex.Message}");
            }
        }

        public async Task<ApiResponse<ComplianceCheckResult>> VerifyAttachmentAsync(Guid attachmentId, VerifyAttachmentRequest request, Guid userId)
        {
            var validationResult = await _verifyValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                return ApiResponse<ComplianceCheckResult>.Failure(errors);
            }

            try
            {
                var attachment = await _attachmentRepository.GetByIdAsync(attachmentId);
                if (attachment == null)
                {
                    return ApiResponse<ComplianceCheckResult>.Failure("Attachment not found.");
                }

                var oldStatus = attachment.Status;
                if (oldStatus != request.Status)
                {
                    if (oldStatus == DocumentStatus.VERIFIED || oldStatus == DocumentStatus.REJECTED)
                    {
                        return ApiResponse<ComplianceCheckResult>.Failure($"Cannot transition attachment from {oldStatus} to {request.Status}.");
                    }
                }

                attachment.Status = request.Status;
                attachment.VerifiedBy = userId;
                attachment.VerifiedAt = DateTime.UtcNow;

                if (request.Status == DocumentStatus.REJECTED)
                {
                    attachment.RejectionReason = request.RejectionReason;
                }
                else
                {
                    attachment.RejectionReason = null;
                }

                await _attachmentRepository.UpdateAttachmentAsync(attachment);

                // Create Audit History record
                var audit = new AttachmentAuditHistory
                {
                    HistoryId = Guid.NewGuid(),
                    AttachmentId = attachment.AttachmentId,
                    PreviousStatus = oldStatus,
                    NewStatus = request.Status,
                    Reason = request.Status == DocumentStatus.REJECTED ? request.RejectionReason : "Attachment approved.",
                    ChangedBy = userId,
                    ChangedAt = DateTime.UtcNow
                };

                await _attachmentRepository.AddAuditHistoryAsync(audit);
                await _attachmentRepository.SaveChangesAsync();

                // Compliance Evaluation (Only for WarehouseReceipt and WarehouseReceiptItem)
                var complianceResult = new ComplianceCheckResult { Passed = true };

                Guid? receiptId = attachment.WarehouseReceiptId;
                if (receiptId == null && attachment.WarehouseReceiptItemId.HasValue)
                {
                    receiptId = attachment.WarehouseReceiptItem?.ReceiptId;
                }

                if (receiptId.HasValue)
                {
                    var receipt = await _receiptRepository.GetByIdAsync(receiptId.Value);
                    if (receipt != null)
                    {
                        var directAttachments = await _attachmentRepository.GetAttachmentsByReceiptIdAsync(receiptId.Value);
                        var allAttachments = new Dictionary<Guid, WarehouseEvidenceAttachment>();

                        foreach (var att in directAttachments)
                        {
                            allAttachments[att.AttachmentId] = att;
                        }

                        foreach (var item in receipt.WarehouseReceiptItems)
                        {
                            var itemAttachments = await _attachmentRepository.GetAttachmentsByReceiptItemIdAsync(item.ItemId);
                            foreach (var att in itemAttachments)
                            {
                                allAttachments[att.AttachmentId] = att;
                            }
                        }

                        var engine = new ComplianceRulesEngine();
                        complianceResult = engine.ValidateReceipt(receipt, allAttachments.Values);
                    }
                }

                return ApiResponse<ComplianceCheckResult>.SuccessResponse(complianceResult, "Attachment verified successfully.");
            }
            catch (Exception ex)
            {
                return ApiResponse<ComplianceCheckResult>.Failure($"Verification failed: {ex.Message}");
            }
        }

        public async Task<ApiResponse<AttachmentResponse>> GetAttachmentAsync(Guid attachmentId)
        {
            try
            {
                var attachment = await _attachmentRepository.GetByIdAsync(attachmentId);
                if (attachment == null)
                {
                    return ApiResponse<AttachmentResponse>.Failure("Attachment not found.");
                }

                return ApiResponse<AttachmentResponse>.SuccessResponse(MapToResponse(attachment));
            }
            catch (Exception ex)
            {
                return ApiResponse<AttachmentResponse>.Failure($"Failed to retrieve attachment: {ex.Message}");
            }
        }

        public async Task<ApiResponse<List<AttachmentResponse>>> GetAttachmentsByReceiptAsync(Guid receiptId)
        {
            try
            {
                var attachments = await _attachmentRepository.GetAttachmentsByReceiptIdAsync(receiptId);
                var responses = attachments.Select(MapToResponse).ToList();
                return ApiResponse<List<AttachmentResponse>>.SuccessResponse(responses);
            }
            catch (Exception ex)
            {
                return ApiResponse<List<AttachmentResponse>>.Failure($"Failed to retrieve attachments: {ex.Message}");
            }
        }

        public async Task<ApiResponse<List<AttachmentResponse>>> GetAttachmentsByReceiptItemAsync(Guid receiptItemId)
        {
            try
            {
                var attachments = await _attachmentRepository.GetAttachmentsByReceiptItemIdAsync(receiptItemId);
                var responses = attachments.Select(MapToResponse).ToList();
                return ApiResponse<List<AttachmentResponse>>.SuccessResponse(responses);
            }
            catch (Exception ex)
            {
                return ApiResponse<List<AttachmentResponse>>.Failure($"Failed to retrieve attachments: {ex.Message}");
            }
        }

        private static AttachmentResponse MapToResponse(WarehouseEvidenceAttachment a)
        {
            return new AttachmentResponse
            {
                AttachmentId = a.AttachmentId,
                FileName = a.FileName,
                FilePath = a.FilePath,
                FileUrl = a.FileUrl,
                FileSize = a.FileSize,
                ContentType = a.ContentType,
                Format = a.Format.ToString(),
                Category = a.Category.ToString(),
                SubCategory = a.SubCategory.ToString(),
                Status = a.Status.ToString(),
                DocumentNumber = a.DocumentNumber,
                Issuer = a.Issuer,
                IssueDate = a.IssueDate,
                ExpiryDate = a.ExpiryDate,
                CapturedValue = a.CapturedValue,
                SealNumber = a.SealNumber,
                RejectionReason = a.RejectionReason,
                VerifiedBy = a.VerifiedBy,
                VerifiedAt = a.VerifiedAt,
                WarehouseReceiptId = a.WarehouseReceiptId,
                WarehouseReceiptItemId = a.WarehouseReceiptItemId,
                InventoryAdjustmentId = a.InventoryAdjustmentId,
                OutboundOrderId = a.OutboundOrderId,
                CreatedAt = a.CreatedAt,
                CreatedBy = a.CreatedBy
            };
        }
    }
}
