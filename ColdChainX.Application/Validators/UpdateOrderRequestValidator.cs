using ColdChainX.Application.DTOs.Orders;
using FluentValidation;

namespace ColdChainX.Application.Validators;

public class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    private const long MaxDocumentSizeBytes = 10 * 1024 * 1024;

    public UpdateOrderRequestValidator()
    {
        RuleFor(request => request.ItemName)
            .NotEmpty()
            .MaximumLength(255)
            .When(request => request.ItemName != null);

        RuleFor(request => request.Category)
            .NotEmpty()
            .Must(category => CreateOrderRequestValidator.AllowedCategories.Contains(category))
            .WithMessage($"Category must be one of: {string.Join(", ", CreateOrderRequestValidator.AllowedCategories)}")
            .When(request => request.Category != null);

        RuleFor(request => request.TempCondition)
            .InclusiveBetween(-18m, 5m)
            .When(request => request.TempCondition.HasValue);

        RuleFor(request => request.ExpectedWeightKg)
            .GreaterThan(0)
            .When(request => request.ExpectedWeightKg.HasValue);

        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .When(request => request.Quantity.HasValue);

        RuleFor(request => request.CustomerProvidedTotalCbm)
            .GreaterThan(0)
            .When(request => request.CustomerProvidedTotalCbm.HasValue);

        RuleFor(request => request.PackagingType)
            .NotEmpty()
            .Must(ContainsOnlyAllowedPackagingTypes)
            .WithMessage($"Packaging_Type must contain only: {string.Join(", ", CreateOrderRequestValidator.AllowedPackagingTypes)}")
            .When(request => request.PackagingType != null);

        RuleFor(request => request.LengthCm)
            .GreaterThan(0)
            .When(request => request.LengthCm.HasValue);
        RuleFor(request => request.WidthCm)
            .GreaterThan(0)
            .When(request => request.WidthCm.HasValue);
        RuleFor(request => request.HeightCm)
            .GreaterThan(0)
            .When(request => request.HeightCm.HasValue);

        RuleFor(request => request.DestAddressText)
            .NotEmpty()
            .MaximumLength(500)
            .When(request => request.DestAddressText != null);

        RuleFor(request => request.ScheduleId)
            .NotEqual(Guid.Empty)
            .When(request => request.ScheduleId.HasValue);
        RuleFor(request => request.DropoffStopId)
            .NotEqual(Guid.Empty)
            .When(request => request.DropoffStopId.HasValue);

        RuleFor(request => request.ReceiverName)
            .NotEmpty()
            .MaximumLength(100)
            .When(request => request.ReceiverName != null);
        RuleFor(request => request.ReceiverPhone)
            .NotEmpty()
            .MaximumLength(20)
            .Must(BeAValidPhoneNumber)
            .WithMessage("Receiver_Phone must contain between 8 and 15 digits")
            .When(request => request.ReceiverPhone != null);

        RuleForEach(request => request.LegalDocuments)
            .Must(file => file.Length > 0 && file.Length <= MaxDocumentSizeBytes)
            .WithMessage("Legal_Documents files must be non-empty and no larger than 10 MB")
            .When(request => request.LegalDocuments != null);
        RuleForEach(request => request.CargoPhotos)
            .Must(file => file.Length > 0 && file.Length <= MaxDocumentSizeBytes)
            .WithMessage("Cargo_Photos files must be non-empty and no larger than 10 MB")
            .When(request => request.CargoPhotos != null);
    }

    private static bool ContainsOnlyAllowedPackagingTypes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(packagingType => CreateOrderRequestValidator.AllowedPackagingTypes.Contains(packagingType));
    }

    private static bool BeAValidPhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var digitCount = value.Count(char.IsDigit);
        return digitCount is >= 8 and <= 15
            && value.All(character => char.IsDigit(character)
                || character is '+' or ' ' or '-' or '(' or ')');
    }
}
