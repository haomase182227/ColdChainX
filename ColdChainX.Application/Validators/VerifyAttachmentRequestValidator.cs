using FluentValidation;
using ColdChainX.Application.DTOs.Attachment;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Validators
{
    public class VerifyAttachmentRequestValidator : AbstractValidator<VerifyAttachmentRequest>
    {
        public VerifyAttachmentRequestValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum().WithMessage("Invalid status value.");

            RuleFor(x => x.RejectionReason)
                .NotEmpty().WithMessage("Rejection reason is required when status is REJECTED.")
                .MaximumLength(255).WithMessage("Rejection reason cannot exceed 255 characters.")
                .When(x => x.Status == DocumentStatus.REJECTED);
        }
    }
}
