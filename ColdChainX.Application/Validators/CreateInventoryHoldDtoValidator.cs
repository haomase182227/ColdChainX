using FluentValidation;
using ColdChainX.Application.DTOs.Inventory;
using System.Linq;

namespace ColdChainX.Application.Validators
{
    public class CreateInventoryHoldDtoValidator : AbstractValidator<CreateInventoryHoldDto>
    {
        private static readonly string[] AllowedReasons = { "TEMP_EXCURSION", "DAMAGED", "EXPIRED", "QA_QUARANTINE", "CUSTOMS_HOLD" };

        public CreateInventoryHoldDtoValidator()
        {
            RuleFor(x => x.StockId).NotEmpty().WithMessage("Stock ID is required.");
            RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Hold quantity must be greater than 0.");
            RuleFor(x => x.ReasonCode)
                .NotEmpty().WithMessage("Reason code is required.")
                .Must(reason => AllowedReasons.Contains(reason))
                .WithMessage($"Reason code must be one of: {string.Join(", ", AllowedReasons)}.");
            RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Notes cannot exceed 500 characters.");
        }
    }
}
