using FluentValidation;
using ColdChainX.Application.DTOs.WarehouseZone;

namespace ColdChainX.Application.Validators
{
    public class UpdateWarehouseZoneRequestValidator : AbstractValidator<UpdateWarehouseZoneRequest>
    {
        public UpdateWarehouseZoneRequestValidator()
        {
            RuleFor(z => z.ZoneCode)
                .NotEmpty().WithMessage("ZoneCode is required.")
                .Length(3, 20).WithMessage("ZoneCode must be between 3 and 20 characters.")
                .Matches("^[A-Z0-9\\-]+$").WithMessage("ZoneCode must contain only uppercase alphanumeric characters and dashes.");

            RuleFor(z => z.ZoneName)
                .NotEmpty().WithMessage("ZoneName is required.")
                .MaximumLength(100).WithMessage("ZoneName cannot exceed 100 characters.");

            RuleFor(z => z.ZoneType)
                .NotEmpty().WithMessage("ZoneType is required.")
                .Must(t => t == "RECEIVING" || t == "STORAGE" || t == "PICKING" || t == "SHIPPING" || t == "QC" || t == "QUARANTINE")
                .WithMessage("ZoneType must be one of: RECEIVING, STORAGE, PICKING, SHIPPING, QC, QUARANTINE.");

            RuleFor(z => z.StorageType)
                .NotEmpty().WithMessage("StorageType is required.")
                .Must(t => t == "RACK" || t == "BULK" || t == "SHELF")
                .WithMessage("StorageType must be one of: RACK, BULK, SHELF.");

            RuleFor(z => z.MaxCapacityPallets)
                .GreaterThan(0).WithMessage("MaxCapacityPallets must be greater than 0.");

            RuleFor(z => z.Status)
                .NotEmpty().WithMessage("Status is required.")
                .Must(s => s == "ACTIVE" || s == "INACTIVE" || s == "MAINTENANCE")
                .WithMessage("Status must be one of: ACTIVE, INACTIVE, MAINTENANCE.");

            // Temperature min/max range checks
            When(z => z.TemperatureMin.HasValue && z.TemperatureMax.HasValue, () =>
            {
                RuleFor(z => z.TemperatureMax)
                    .GreaterThan(z => z.TemperatureMin)
                    .WithMessage("TemperatureMax must be greater than TemperatureMin.");
            });

            When(z => z.TemperatureMin.HasValue && !z.TemperatureMax.HasValue, () =>
            {
                RuleFor(z => z.TemperatureMax)
                    .NotNull().WithMessage("TemperatureMax is required if TemperatureMin is specified.");
            });

            When(z => !z.TemperatureMin.HasValue && z.TemperatureMax.HasValue, () =>
            {
                RuleFor(z => z.TemperatureMin)
                    .NotNull().WithMessage("TemperatureMin is required if TemperatureMax is specified.");
            });
        }
    }
}
