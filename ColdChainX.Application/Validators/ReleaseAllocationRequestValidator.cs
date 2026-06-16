using FluentValidation;
using ColdChainX.Application.DTOs.Inventory;

namespace ColdChainX.Application.Validators
{
    public class ReleaseAllocationRequestValidator : AbstractValidator<ReleaseAllocationRequest>
    {
        public ReleaseAllocationRequestValidator()
        {
            RuleFor(x => x.ReferenceDocumentId)
                .NotEmpty().WithMessage("Reference document ID is required.");
        }
    }
}
