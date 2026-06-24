using Xunit;
using ColdChainX.Application.Validators;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using System;
using System.Collections.Generic;
using ColdChainX.Core.Enums;

namespace ColdChainX.UnitTests
{
    public class WarehouseReceiptValidatorTests
    {
        private readonly InboundQCRequestValidator _qcValidator;
        private readonly UpdateMeasurementsRequestValidator _measurementsValidator;

        public WarehouseReceiptValidatorTests()
        {
            _qcValidator = new InboundQCRequestValidator();
            _measurementsValidator = new UpdateMeasurementsRequestValidator();
        }

        [Fact]
        public void InboundQCRequest_WithValidData_ShouldPass()
        {
            var request = new InboundQCRequest
            {
                OrderId = Guid.NewGuid(),
                WarehouseId = Guid.NewGuid(),
                RecordedTemperature = -15.5m,
                DelivererName = "Driver Name",
                Note = "Some note"
            };

            var result = _qcValidator.Validate(request);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void InboundQCRequest_WithEmptyFields_ShouldFail()
        {
            var request = new InboundQCRequest
            {
                OrderId = Guid.Empty,
                WarehouseId = Guid.Empty,
                RecordedTemperature = 0,
                DelivererName = "",
                Note = null
            };

            var result = _qcValidator.Validate(request);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == "OrderId");
            Assert.Contains(result.Errors, e => e.PropertyName == "WarehouseId");
            Assert.Contains(result.Errors, e => e.PropertyName == "DelivererName");
        }

        [Fact]
        public void UpdateMeasurementsRequest_WithValidData_ShouldPass()
        {
            var request = new UpdateMeasurementsRequest
            {
                Items = new List<InboundItemMeasurement>
                {
                    new InboundItemMeasurement
                    {
                        ItemName = "Fish",
                        ActualQty = 10,
                        CountryOfOrigin = "Vietnam",
                        ProductCategory = ProductCategory.SEAFOOD
                    }
                }
            };

            var result = _measurementsValidator.Validate(request);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void UpdateMeasurementsRequest_WithInvalidItem_ShouldFail()
        {
            var request = new UpdateMeasurementsRequest
            {
                Items = new List<InboundItemMeasurement>
                {
                    new InboundItemMeasurement
                    {
                        ItemName = "",
                        ActualQty = 0,
                        CountryOfOrigin = "",
                        ProductCategory = (ProductCategory)999
                    }
                }
            };

            var result = _measurementsValidator.Validate(request);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].ItemName"));
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].ActualQty"));
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].CountryOfOrigin"));
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].ProductCategory"));
        }
    }
}
