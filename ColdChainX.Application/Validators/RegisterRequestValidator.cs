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
                .MaximumLength(200).WithMessage("Full name must not exceed 200 characters");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("Invalid email format");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters");

            RuleFor(x => x.PhoneNumber)
                .Matches(@"^\d{10,15}$").WithMessage("Phone number must be 10-15 digits")
                .When(x => !string.IsNullOrEmpty(x.PhoneNumber));

            RuleFor(x => x.Role)
                .IsInEnum().WithMessage("Invalid role value")
                .Must(role => Enum.IsDefined(typeof(Role), role))
                .WithMessage($"Role must be one of: {string.Join(", ", Enum.GetNames(typeof(Role)))}");
        }
    }
}
