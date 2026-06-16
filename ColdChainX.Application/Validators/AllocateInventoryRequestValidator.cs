using FluentValidation;
using ColdChainX.Application.DTOs.Inventory;

namespace ColdChainX.Application.Validators
{
    public class AllocateInventoryRequestValidator : AbstractValidator<AllocateInventoryRequest>
    {
        public AllocateInventoryRequestValidator()
        {
            RuleFor(x => x.ReferenceDocumentId)
                .NotEmpty().WithMessage("Reference document ID is required.");

            RuleFor(x => x.Items)
                .NotEmpty().WithMessage("Allocation items list cannot be empty.");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ItemCode)
                    .NotEmpty().WithMessage("Item code is required.");

                item.RuleFor(i => i.Quantity)
                    .GreaterThan(0).WithMessage("Allocation quantity must be greater than zero.");
            });
        }
    }
}
