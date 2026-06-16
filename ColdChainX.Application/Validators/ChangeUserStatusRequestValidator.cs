using FluentValidation;
using ColdChainX.Application.DTOs;
using ColdChainX.Core.Enums;
using System;

namespace ColdChainX.Application.Validators
{
    public class ChangeUserStatusRequestValidator : AbstractValidator<ChangeUserStatusRequest>
    {
        public ChangeUserStatusRequestValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum().WithMessage("Invalid status value")
                .Must(status => Enum.IsDefined(typeof(UserStatus), status))
                .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames(typeof(UserStatus)))}");
        }
    }
}
