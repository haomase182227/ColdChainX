using System;
using System.IO;
using System.Linq;
using FluentValidation;
using ColdChainX.Application.DTOs.Attachment;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Validators
{
    public class UploadAttachmentRequestValidator : AbstractValidator<UploadAttachmentRequest>
    {
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf" };
        private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

        public UploadAttachmentRequestValidator()
        {
            RuleFor(x => x.File)
                .NotNull().WithMessage("File is required.");

            RuleFor(x => x.File)
                .Must(file => file.Length > 0).WithMessage("File cannot be empty.")
                .Must(file => file.Length <= MaxFileSizeBytes).WithMessage("File size must not exceed 50 MB.")
                .Must(file =>
                {
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    return AllowedExtensions.Contains(ext);
                }).WithMessage("Unsupported file format. Supported formats are: JPG, JPEG, PNG, PDF.")
                .When(x => x.File != null);

            // Mandatory attachment target (Exclusive OR constraint)
            RuleFor(x => x)
                .Must(x =>
                {
                    int targetCount = 0;
                    if (x.WarehouseReceiptId.HasValue) targetCount++;
                    if (x.WarehouseReceiptItemId.HasValue) targetCount++;
                    if (x.InventoryAdjustmentId.HasValue) targetCount++;
                    if (x.OutboundOrderId.HasValue) targetCount++;
                    return targetCount == 1;
                })
                .WithMessage("Attachment must be linked to exactly one target (WarehouseReceiptId, WarehouseReceiptItemId, InventoryAdjustmentId, or OutboundOrderId).");

            // SealNumber required when AttachmentSubCategory == SEAL_PHOTO
            RuleFor(x => x.SealNumber)
                .NotEmpty().WithMessage("SealNumber is required when sub-category is SEAL_PHOTO.")
                .When(x => x.SubCategory == AttachmentSubCategory.SEAL_PHOTO);
        }
    }
}
