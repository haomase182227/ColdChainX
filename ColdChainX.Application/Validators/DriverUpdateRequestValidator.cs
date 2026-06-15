using ColdChainX.Application.DTOs;
using FluentValidation;

namespace ColdChainX.Application.Validators
{
    public class DriverUpdateRequestValidator : AbstractValidator<DriverUpdateRequest>
    {
        private static readonly string[] ValidDriverStatuses =
            { "AVAILABLE", "ON_TRIP", "OFFLINE", "INACTIVE" };

        public DriverUpdateRequestValidator()
        {
            RuleFor(x => x.DateOfBirth)
                .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
                .When(x => x.DateOfBirth.HasValue)
                .WithMessage("Date of birth cannot be in the future");

            RuleFor(x => x.Status)
                .MaximumLength(20).WithMessage("Status must not exceed 20 characters")
                .Must(status => ValidDriverStatuses.Contains(status!.Trim(), StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Status must be one of: {string.Join(", ", ValidDriverStatuses)}")
                .When(x => !string.IsNullOrWhiteSpace(x.Status));
        }
    }
}