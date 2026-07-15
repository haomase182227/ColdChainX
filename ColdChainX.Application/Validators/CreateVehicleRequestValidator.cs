using ColdChainX.Application.DTOs.Fleet;
using FluentValidation;

namespace ColdChainX.Application.Validators;

public class CreateVehicleRequestValidator : AbstractValidator<CreateVehicleRequest>
{
    public CreateVehicleRequestValidator()
    {
        RuleFor(x => x.TruckPlate)
            .NotEmpty().WithMessage("Truck plate is required")
            .MaximumLength(20).WithMessage("Truck plate must not exceed 20 characters");

        RuleFor(x => x.VehicleType)
            .NotEmpty().WithMessage("Vehicle type is required")
            .MaximumLength(50).WithMessage("Vehicle type must not exceed 50 characters");

        RuleFor(x => x.MaxWeight)
            .GreaterThan(0).WithMessage("Max weight must be greater than zero");

        RuleFor(x => x.MaxCbm)
            .GreaterThan(0).WithMessage("Max CBM must be greater than zero");

        RuleFor(x => x.InnerLengthCm)
            .GreaterThan(0).WithMessage("Inner length must be greater than zero");

        RuleFor(x => x.InnerWidthCm)
            .GreaterThan(0).WithMessage("Inner width must be greater than zero");

        RuleFor(x => x.InnerHeightCm)
            .GreaterThan(0).WithMessage("Inner height must be greater than zero");

        RuleFor(x => x)
            .Must(x => x.InnerLengthCm <= 0 || x.InnerWidthCm <= 0 || x.InnerHeightCm <= 0
                || x.MaxCbm <= x.InnerLengthCm * x.InnerWidthCm * x.InnerHeightCm / 1_000_000m)
            .WithMessage("Max CBM cannot exceed the volume calculated from the inner dimensions");

        RuleFor(x => x.MinTemp)
            .InclusiveBetween(-100m, 100m).WithMessage("Min temp must be between -100 and 100");

        RuleFor(x => x.MaxTemp)
            .InclusiveBetween(-100m, 100m).WithMessage("Max temp must be between -100 and 100")
            .GreaterThanOrEqualTo(x => x.MinTemp)
            .WithMessage("Max temp must be greater than or equal to min temp");
    }
}
