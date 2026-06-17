using FluentValidation;
using ColdChainX.Application.DTOs.Inventory;

namespace ColdChainX.Application.Validators
{
    public class InventoryAdjustmentRequestValidator : AbstractValidator<InventoryAdjustmentRequest>
    {
        public InventoryAdjustmentRequestValidator()
        {
            RuleFor(x => x.StockId)
                .NotEmpty().WithMessage("Stock ID is required.");
            
            RuleFor(x => x.AdjustmentType)
                .IsInEnum().WithMessage("A valid adjustment type (DAMAGED, EXPIRED, LOST, FOUND, CYCLE_COUNT, QUALITY_HOLD) is required.");

            RuleFor(x => x.Reason)
                .NotEmpty().WithMessage("Reason notes are mandatory.")
                .MinimumLength(5).WithMessage("Please provide a descriptive reason (minimum 5 characters).");

            RuleFor(x => x.Quantity)
                .Must((req, qty) => !req.IsAbsoluteCount || qty >= 0)
                .WithMessage("Physical counted quantity cannot be negative when IsAbsoluteCount is true.");

            RuleFor(x => x.Pallets)
                .Must((req, pal) => !req.IsAbsoluteCount || pal >= 0)
                .WithMessage("Physical counted pallet count cannot be negative when IsAbsoluteCount is true.");
        }
    }
}
