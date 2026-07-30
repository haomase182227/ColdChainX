using ColdChainX.Application.DTOs;
using FluentValidation;

namespace ColdChainX.Application.Validators
{
    public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
    {
        public ChangePasswordRequestValidator()
        {
            RuleFor(x => x.CurrentPassword)
                .NotEmpty().WithMessage("Current password is required");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("New password is required")
                .MinimumLength(8).WithMessage("New password must be at least 8 characters")
                .NotEqual(x => x.CurrentPassword)
                .WithMessage("New password must be different from current password");
        }
    }
}
