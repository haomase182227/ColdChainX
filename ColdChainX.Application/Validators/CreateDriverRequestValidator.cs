using FluentValidation;
using ColdChainX.Application.DTOs;

namespace ColdChainX.Application.Validators
{
    public class CreateDriverRequestValidator : AbstractValidator<CreateDriverRequest>
    {
        public CreateDriverRequestValidator()
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Full name is required")
                .MaximumLength(100).WithMessage("Full name must not exceed 100 characters");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("Invalid email format")
                .MaximumLength(100).WithMessage("Email must not exceed 100 characters");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters");

            RuleFor(x => x.DateOfBirth)
                .NotEmpty().WithMessage("Date of birth is required")
                .LessThan(DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("Date of birth must be in the past");

            RuleFor(x => x.Username)
                .MaximumLength(50).WithMessage("Username must not exceed 50 characters")
                .When(x => !string.IsNullOrWhiteSpace(x.Username));

            // License validation: if one field is provided, others should be too
            When(x => !string.IsNullOrWhiteSpace(x.LicenseNumber) ||
                      !string.IsNullOrWhiteSpace(x.LicenseClass) ||
                      x.IssueDate.HasValue ||
                      x.ExpiryDate.HasValue, () =>
            {
                RuleFor(x => x.LicenseNumber)
                    .NotEmpty().WithMessage("License number is required when providing license information")
                    .MaximumLength(50).WithMessage("License number must not exceed 50 characters");

                RuleFor(x => x.LicenseClass)
                    .NotEmpty().WithMessage("License class is required when providing license information")
                    .MaximumLength(20).WithMessage("License class must not exceed 20 characters");

                RuleFor(x => x.IssueDate)
                    .NotEmpty().WithMessage("Issue date is required when providing license information");

                RuleFor(x => x.ExpiryDate)
                    .NotEmpty().WithMessage("Expiry date is required when providing license information")
                    .GreaterThan(x => x.IssueDate ?? DateOnly.MinValue)
                    .WithMessage("Expiry date must be after issue date");

                RuleFor(x => x.DocumentUrl)
                    .MaximumLength(500).WithMessage("Document URL must not exceed 500 characters")
                    .When(x => !string.IsNullOrWhiteSpace(x.DocumentUrl));
            });
        }
    }
}
