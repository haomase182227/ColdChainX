using ColdChainX.Application.DTOs;
using FluentValidation;

namespace ColdChainX.Application.Validators
{
    public class DriverUpdateRequestValidator : AbstractValidator<DriverUpdateRequest>
    {
        public DriverUpdateRequestValidator()
        {
            RuleFor(x => x.DateOfBirth)
                .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
                .When(x => x.DateOfBirth.HasValue)
                .WithMessage("Date of birth cannot be in the future");

            RuleFor(x => x.Status)
                .MaximumLength(20).WithMessage("Status must not exceed 20 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.Status));
        }
    }
}