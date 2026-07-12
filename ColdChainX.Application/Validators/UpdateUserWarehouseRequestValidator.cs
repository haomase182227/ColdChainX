using FluentValidation;
using ColdChainX.Application.DTOs;

namespace ColdChainX.Application.Validators
{
    public class UpdateUserWarehouseRequestValidator : AbstractValidator<UpdateUserWarehouseRequest>
    {
        public UpdateUserWarehouseRequestValidator()
        {
            RuleFor(x => x.WarehouseId)
                .NotEmpty().WithMessage("WarehouseId is required");
        }
    }
}
