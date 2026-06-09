using FluentValidation;
using ColdChainX.Application.DTOs;

namespace ColdChainX.Application.Validators
{
    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Full name is required")
                .MaximumLength(100).WithMessage("Full name must not exceed 100 characters");

            RuleFor(x => x.Username)
                .MaximumLength(50).WithMessage("Username must not exceed 50 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.Username));

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("Invalid email format")
                .MaximumLength(255).WithMessage("Email must not exceed 255 characters");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters");

            // Role is the role name stored in the database (e.g. "Customer", "Admin")
            RuleFor(x => x.Role)
                .NotEmpty().WithMessage("Role is required")
                .MaximumLength(100).WithMessage("Role must not exceed 100 characters");

            RuleFor(x => x.CompanyName)
                .MaximumLength(200).WithMessage("Company name must not exceed 200 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.CompanyName));

            // DateOfBirth is optional; if provided it must be a reasonable past date
            RuleFor(x => x.DateOfBirth)
                .Must(d => d == null || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("Date of birth must be in the past");
        }
    }
}
