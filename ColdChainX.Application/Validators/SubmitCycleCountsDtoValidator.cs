using FluentValidation;
using ColdChainX.Application.DTOs.CycleCount;

namespace ColdChainX.Application.Validators
{
    public class SubmitCycleCountsDtoValidator : AbstractValidator<SubmitCycleCountsDto>
    {
        public SubmitCycleCountsDtoValidator()
        {
            RuleFor(x => x.Counts)
                .NotEmpty().WithMessage("Counts list cannot be empty.");
                
            RuleForEach(x => x.Counts).SetValidator(new SubmitEntryCountDtoValidator());
        }
    }

    public class SubmitEntryCountDtoValidator : AbstractValidator<SubmitEntryCountDto>
    {
        public SubmitEntryCountDtoValidator()
        {
            RuleFor(x => x.EntryId).NotEmpty().WithMessage("Entry ID is required.");
            RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0).WithMessage("Counted quantity cannot be negative.");
            RuleFor(x => x.CountedPallets).GreaterThanOrEqualTo(0).WithMessage("Counted pallets cannot be negative.");
        }
    }
}
