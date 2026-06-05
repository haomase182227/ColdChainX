using FluentValidation;
using ColdChainX.Application.DTOs;
using ColdChainX.Core.Enums;
using System;

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

            RuleFor(x => x.Role)
                .IsInEnum().WithMessage("Invalid role value")
                .Must(role => Enum.IsDefined(typeof(Role), role))
                .WithMessage($"Role must be one of: {string.Join(", ", Enum.GetNames(typeof(Role)))}");
        }
    }
}
