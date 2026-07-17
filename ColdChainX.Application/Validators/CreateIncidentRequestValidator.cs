using ColdChainX.Application.DTOs.Incident;
using FluentValidation;

namespace ColdChainX.Application.Validators;

public class CreateIncidentRequestValidator : AbstractValidator<CreateIncidentRequest>
{
    public CreateIncidentRequestValidator()
    {
        RuleFor(x => x.IncidentType)
            .NotNull().WithMessage("Incident type is required.")
            .IsInEnum().WithMessage("Incident type is invalid.");

        RuleFor(x => x.Severity)
            .NotNull().WithMessage("Severity is required.")
            .IsInEnum().WithMessage("Severity is invalid.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.");

        RuleFor(x => x.CurrentLatitude)
            .InclusiveBetween(-90m, 90m)
            .When(x => x.CurrentLatitude.HasValue)
            .WithMessage("Current latitude must be between -90 and 90.");

        RuleFor(x => x.CurrentLongitude)
            .InclusiveBetween(-180m, 180m)
            .When(x => x.CurrentLongitude.HasValue)
            .WithMessage("Current longitude must be between -180 and 180.");

        RuleFor(x => x.DriverPaidAmount)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Driver-paid amount cannot be negative.");
    }
}
