using ColdChainX.Application.DTOs;
using ColdChainX.Core.Enums;
using FluentValidation;

namespace ColdChainX.Application.Validators
{
    public class VehicleCreateRequestValidator : AbstractValidator<VehicleCreateRequest>
    {
        public VehicleCreateRequestValidator()
        {
            RuleFor(x => x.TruckPlate)
                .NotEmpty().WithMessage("Truck plate is required")
                .MaximumLength(20).WithMessage("Truck plate must not exceed 20 characters");

            RuleFor(x => x.Brand)
                .MaximumLength(50).WithMessage("Brand must not exceed 50 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.Brand));

            RuleFor(x => x.ManufactureYear)
                .InclusiveBetween(1900, 2100).When(x => x.ManufactureYear.HasValue)
                .WithMessage("Manufacture year must be between 1900 and 2100");

            RuleFor(x => x.ChassisNumber)
                .MaximumLength(50).WithMessage("Chassis number must not exceed 50 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.ChassisNumber));

            RuleFor(x => x.EngineNumber)
                .MaximumLength(50).WithMessage("Engine number must not exceed 50 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.EngineNumber));

            RuleFor(x => x.StandardFuelLiters)
                .GreaterThan(0).When(x => x.StandardFuelLiters.HasValue)
                .WithMessage("Standard fuel liters must be greater than zero");

            RuleFor(x => x.VehicleType)
                .IsInEnum().WithMessage($"Vehicle type must be one of: {string.Join(", ", Enum.GetNames(typeof(VehicleType)))}");

            RuleFor(x => x.MaxWeight)
                .GreaterThan(0).WithMessage("Max weight must be greater than zero");

            RuleFor(x => x.MaxCbm)
                .GreaterThan(0).WithMessage("Max CBM must be greater than zero");

            RuleFor(x => x.MinTemp)
                .InclusiveBetween(-100m, 100m).WithMessage("Min temp must be between -100 and 100");

            RuleFor(x => x.MaxTemp)
                .InclusiveBetween(-100m, 100m).WithMessage("Max temp must be between -100 and 100")
                .GreaterThanOrEqualTo(x => x.MinTemp).WithMessage("Max temp must be greater than or equal to min temp");

            RuleFor(x => x.Status)
                .IsInEnum().WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames(typeof(VehicleStatus)))}");
        }
    }
}