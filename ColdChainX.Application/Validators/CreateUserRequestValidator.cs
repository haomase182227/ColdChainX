using FluentValidation;
using ColdChainX.Application.DTOs;
using ColdChainX.Core.Enums;
using System;

namespace ColdChainX.Application.Validators
{
    public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
    {
        public CreateUserRequestValidator()
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Full name is required")
                .MaximumLength(100).WithMessage("Full name must not exceed 100 characters");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("Invalid email format")
                .MaximumLength(255).WithMessage("Email must not exceed 255 characters");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters");

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(50).WithMessage("Phone number must not exceed 50 characters")
                .When(x => !string.IsNullOrEmpty(x.PhoneNumber));

            RuleFor(x => x.Role)
                .NotEmpty().WithMessage("Role is required")
                .MaximumLength(50).WithMessage("Role must not exceed 50 characters");

            RuleFor(x => x.Status)
                .IsInEnum().WithMessage("Invalid status value")
                .Must(status => Enum.IsDefined(typeof(UserStatus), status))
                .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames(typeof(UserStatus)))}");
        }
    }
}
