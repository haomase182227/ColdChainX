using FluentValidation;
using ColdChainX.Application.DTOs.Warehouse;

namespace ColdChainX.Application.Validators
{
    public class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
    {
        public CreateWarehouseRequestValidator()
        {
            RuleFor(w => w.WarehouseCode)
                .NotEmpty().WithMessage("WarehouseCode is required.")
                .Length(3, 20).WithMessage("WarehouseCode must be between 3 and 20 characters.")
                .Matches("^[A-Z0-9\\-]+$").WithMessage("WarehouseCode must contain only uppercase alphanumeric characters and dashes.");

            RuleFor(w => w.WarehouseName)
                .NotEmpty().WithMessage("WarehouseName is required.")
                .MaximumLength(100).WithMessage("WarehouseName cannot exceed 100 characters.");

            RuleFor(w => w.WarehouseType)
                .NotEmpty().WithMessage("WarehouseType is required.")
                .Must(t => t == "DRY" || t == "COLD" || t == "BONDED" || t == "CHEMICAL")
                .WithMessage("WarehouseType must be one of: DRY, COLD, BONDED, CHEMICAL.");

            RuleFor(w => w.Status)
                .NotEmpty().WithMessage("Status is required.")
                .Must(s => s == "ACTIVE" || s == "INACTIVE" || s == "MAINTENANCE")
                .WithMessage("Status must be one of: ACTIVE, INACTIVE, MAINTENANCE.");

            RuleFor(w => w.MaxPallets)
                .GreaterThanOrEqualTo(0).WithMessage("MaxPallets must be greater than or equal to 0.");

            RuleFor(w => w.Address)
                .MaximumLength(100).WithMessage("Address cannot exceed 100 characters.");

            // Refrigeration temperature checks
            When(w => w.WarehouseType == "COLD", () =>
            {
                RuleFor(w => w.DefaultMinTemp)
                    .NotNull().WithMessage("DefaultMinTemp is required for cold storage warehouses.");

                RuleFor(w => w.DefaultMaxTemp)
                    .NotNull().WithMessage("DefaultMaxTemp is required for cold storage warehouses.")
                    .GreaterThan(w => w.DefaultMinTemp)
                    .WithMessage("DefaultMaxTemp must be greater than DefaultMinTemp.");
            });

            When(w => w.WarehouseType != "COLD", () =>
            {
                RuleFor(w => w.DefaultMinTemp)
                    .Null().WithMessage("DefaultMinTemp must be null for non-cold storage warehouses.");

                RuleFor(w => w.DefaultMaxTemp)
                    .Null().WithMessage("DefaultMaxTemp must be null for non-cold storage warehouses.");
            });
        }
    }
}
