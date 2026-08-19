using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.Validators;

namespace ColdChainX.UnitTests;

public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    [Fact]
    public async Task Validate_WhenAllCreateOrderFieldsAreProvided_ShouldPass()
    {
        var result = await _validator.ValidateAsync(CreateCompleteRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_WhenRequiredSelectionsAndFilesAreMissing_ShouldFail()
    {
        var request = CreateCompleteRequest();
        request.HasStrongOdor = null;
        request.IsStackable = null;
        request.LegalDocuments.Clear();
        request.CargoPhotos.Clear();

        var result = await _validator.ValidateAsync(request);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.HasStrongOdor));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.IsStackable));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.LegalDocuments));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.CargoPhotos));
    }

    [Fact]
    public async Task Validate_WhenReceiverInformationIsMissing_ShouldFail()
    {
        var request = CreateCompleteRequest();
        request.ReceiverName = string.Empty;
        request.ReceiverPhone = string.Empty;

        var result = await _validator.ValidateAsync(request);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.ReceiverName));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.ReceiverPhone));
    }

    private static CreateOrderRequest CreateCompleteRequest()
    {
        return new CreateOrderRequest
        {
            ItemName = "Frozen seafood",
            Category = "MEAT_SEAFOOD",
            TempCondition = -18,
            ExpectedWeightKg = 100,
            Quantity = 10,
            PackagingType = "Carton Box",
            LengthCm = 100,
            WidthCm = 80,
            HeightCm = 60,
            DestAddressText = "Test delivery address",
            ScheduleId = Guid.NewGuid(),
            DropoffStopId = Guid.NewGuid(),
            ReceiverName = "Nguyen Van A",
            ReceiverPhone = "0901234567",
            HasStrongOdor = false,
            IsStackable = true,
            LegalDocuments = [new FakeFormFile([1], "application/pdf", "document.pdf")],
            CargoPhotos = [new FakeFormFile([2], "image/jpeg", "cargo.jpg")]
        };
    }
}
