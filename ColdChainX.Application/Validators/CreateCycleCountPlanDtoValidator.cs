using FluentValidation;
using ColdChainX.Application.DTOs.CycleCount;

namespace ColdChainX.Application.Validators
{
    public class CreateCycleCountPlanDtoValidator : AbstractValidator<CreateCycleCountPlanDto>
    {
        public CreateCycleCountPlanDtoValidator()
        {
            RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Warehouse ID is required.");
            
            RuleFor(x => x)
                .Must(x => (x.ZoneIds != null && x.ZoneIds.Count > 0) || (x.LocationIds != null && x.LocationIds.Count > 0))
                .WithMessage("At least one target Zone ID or Location ID must be specified.");
        }
    }
}
