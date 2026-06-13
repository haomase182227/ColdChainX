using FluentValidation;
using ColdChainX.Application.DTOs.WarehouseLocation;

namespace ColdChainX.Application.Validators
{
    public class CreateWarehouseLocationRequestValidator : AbstractValidator<CreateWarehouseLocationRequest>
    {
        public CreateWarehouseLocationRequestValidator()
        {
            RuleFor(l => l.LocationCode)
                .NotEmpty().WithMessage("LocationCode is required.")
                .Length(3, 50).WithMessage("LocationCode must be between 3 and 50 characters.")
                .Matches(@"^[A-Z0-9\-\.]+$").WithMessage("LocationCode must contain only uppercase letters, digits, dashes, or dots.");

            RuleFor(l => l.RackCode)
                .MaximumLength(20).WithMessage("RackCode cannot exceed 20 characters.")
                .When(l => l.RackCode != null);

            RuleFor(l => l.BayCode)
                .MaximumLength(20).WithMessage("BayCode cannot exceed 20 characters.")
                .When(l => l.BayCode != null);

            RuleFor(l => l.LevelCode)
                .MaximumLength(20).WithMessage("LevelCode cannot exceed 20 characters.")
                .When(l => l.LevelCode != null);

            RuleFor(l => l.MaxCapacityPallets)
                .GreaterThan(0).WithMessage("MaxCapacityPallets must be greater than 0.");

            RuleFor(l => l.Description)
                .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.")
                .When(l => l.Description != null);
        }
    }
}
