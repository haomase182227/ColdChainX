using FluentValidation;
using ColdChainX.Application.DTOs;
using System;

namespace ColdChainX.Application.Validators
{
    public class ChangeUserRoleRequestValidator : AbstractValidator<ChangeUserRoleRequest>
    {
        public ChangeUserRoleRequestValidator()
        {
            RuleFor(x => x.Role)
                .NotEmpty().WithMessage("Role is required")
                .MaximumLength(50).WithMessage("Role must not exceed 50 characters");
        }
    }
}
