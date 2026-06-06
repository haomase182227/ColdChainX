using ColdChainX.Application.DTOs;
using FluentValidation;

namespace ColdChainX.Application.Validators
{
    public class DriverCreateRequestValidator : AbstractValidator<DriverCreateRequest>
    {
        public DriverCreateRequestValidator()
        {
            RuleFor(x => x.DateOfBirth)
                .NotEqual(default(DateOnly)).WithMessage("Date of birth is required")
                .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("Date of birth cannot be in the future");

            RuleFor(x => x.Status)
                .MaximumLength(20).WithMessage("Status must not exceed 20 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.Status));
        }
    }
}