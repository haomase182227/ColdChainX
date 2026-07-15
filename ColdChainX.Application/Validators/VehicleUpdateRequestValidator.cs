using ColdChainX.Application.DTOs;
using FluentValidation;

namespace ColdChainX.Application.Validators
{
    public class VehicleUpdateRequestValidator : AbstractValidator<VehicleUpdateRequest>
    {
        public VehicleUpdateRequestValidator()
        {
            RuleFor(x => x.TruckPlate)
                .MaximumLength(20).WithMessage("Truck plate must not exceed 20 characters")
                .NotEmpty().When(x => x.TruckPlate != null)
                .WithMessage("Truck plate cannot be empty");

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
                .MaximumLength(50).WithMessage("Vehicle type must not exceed 50 characters")
                .NotEmpty().When(x => x.VehicleType != null)
                .WithMessage("Vehicle type cannot be empty");

            RuleFor(x => x.MaxWeight)
                .GreaterThan(0).When(x => x.MaxWeight.HasValue)
                .WithMessage("Max weight must be greater than zero");

            RuleFor(x => x.MaxCbm)
                .GreaterThan(0).When(x => x.MaxCbm.HasValue)
                .WithMessage("Max CBM must be greater than zero");

            RuleFor(x => x.InnerLengthCm)
                .GreaterThan(0).When(x => x.InnerLengthCm.HasValue)
                .WithMessage("Inner length must be greater than zero");

            RuleFor(x => x.InnerWidthCm)
                .GreaterThan(0).When(x => x.InnerWidthCm.HasValue)
                .WithMessage("Inner width must be greater than zero");

            RuleFor(x => x.InnerHeightCm)
                .GreaterThan(0).When(x => x.InnerHeightCm.HasValue)
                .WithMessage("Inner height must be greater than zero");

            RuleFor(x => x.MinTemp)
                .InclusiveBetween(-100, 100).When(x => x.MinTemp.HasValue)
                .WithMessage("Min temp must be between -100 and 100");

            RuleFor(x => x.MaxTemp)
                .InclusiveBetween(-100, 100).When(x => x.MaxTemp.HasValue)
                .WithMessage("Max temp must be between -100 and 100");

            RuleFor(x => x)
                .Must(x => !x.MinTemp.HasValue || !x.MaxTemp.HasValue || x.MaxTemp.Value >= x.MinTemp.Value)
                .WithMessage("Max temp must be greater than or equal to min temp");

            RuleFor(x => x.Status)
                .MaximumLength(20).WithMessage("Status must not exceed 20 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.Status));
        }
    }
}
