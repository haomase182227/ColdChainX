using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Attachment;
using ColdChainX.Application.Models;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IAttachmentManagementService
    {
        Task<ApiResponse<AttachmentResponse>> UploadAttachmentAsync(UploadAttachmentRequest request, Guid userId);
        Task<ApiResponse<ComplianceCheckResult>> VerifyAttachmentAsync(Guid attachmentId, VerifyAttachmentRequest request, Guid userId);
        Task<ApiResponse<AttachmentResponse>> GetAttachmentAsync(Guid attachmentId);
        Task<ApiResponse<List<AttachmentResponse>>> GetAttachmentsByReceiptAsync(Guid receiptId);
        Task<ApiResponse<List<AttachmentResponse>>> GetAttachmentsByReceiptItemAsync(Guid receiptItemId);
    }
}
