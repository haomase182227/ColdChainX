using ColdChainX.Application.DTOs.Incident;
using FluentValidation;

namespace ColdChainX.Application.Validators;

public class ResolveIncidentRequestValidator : AbstractValidator<ResolveIncidentRequest>
{
    public ResolveIncidentRequestValidator()
    {
        RuleFor(x => x.ResolutionNote)
            .NotEmpty().WithMessage("Resolution note is required.");

        RuleFor(x => x.ReimbursedAmount)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Reimbursed amount cannot be negative.");
    }
}
