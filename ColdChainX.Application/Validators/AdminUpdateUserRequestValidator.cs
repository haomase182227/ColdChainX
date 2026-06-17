using FluentValidation;
using ColdChainX.Application.DTOs;

namespace ColdChainX.Application.Validators
{
    public class AdminUpdateUserRequestValidator : AbstractValidator<AdminUpdateUserRequest>
    {
        public AdminUpdateUserRequestValidator()
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Full name is required")
                .MaximumLength(100).WithMessage("Full name must not exceed 100 characters");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("Invalid email format")
                .MaximumLength(255).WithMessage("Email must not exceed 255 characters");

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(50).WithMessage("Phone number must not exceed 50 characters")
                .When(x => !string.IsNullOrEmpty(x.PhoneNumber));
        }
    }
}
