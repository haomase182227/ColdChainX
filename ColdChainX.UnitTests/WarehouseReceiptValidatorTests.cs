using Xunit;
using ColdChainX.Application.Validators;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using System;
using System.Collections.Generic;
using System.Linq;
using ColdChainX.Core.Enums;

namespace ColdChainX.UnitTests
{
    public class WarehouseReceiptValidatorTests
    {
        private readonly ProcessInboundQCPayloadValidator _qcValidator;
        private readonly UpdateMeasurementsPayloadValidator _measurementsValidator;

        public WarehouseReceiptValidatorTests()
        {
            _qcValidator = new ProcessInboundQCPayloadValidator();
            _measurementsValidator = new UpdateMeasurementsPayloadValidator();
        }

        [Fact]
        public void ProcessInboundQC_WithValidData_ShouldPass()
        {
            var payload = new ProcessInboundQCPayload
            {
                WarehouseReceipt = new InboundQCBlock
                {
                    OrderId = Guid.NewGuid(),
                    WarehouseId = Guid.NewGuid(),
                    RecordedTemperature = -15.5m,
                    DelivererName = "Driver Name",
                    Note = "Some note"
                }
            };

            var result = _qcValidator.Validate(payload);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ProcessInboundQC_WithNullBlock_ShouldFail()
        {
            var payload = new ProcessInboundQCPayload
            {
                WarehouseReceipt = null!
            };

            var result = _qcValidator.Validate(payload);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == "WarehouseReceipt" && e.ErrorMessage.Contains("required"));
        }

        [Fact]
        public void ProcessInboundQC_WithEmptyFields_ShouldFail()
        {
            var payload = new ProcessInboundQCPayload
            {
                WarehouseReceipt = new InboundQCBlock
                {
                    OrderId = Guid.Empty,
                    WarehouseId = Guid.Empty,
                    RecordedTemperature = 0,
                    DelivererName = "",
                    Note = null
                }
            };

            var result = _qcValidator.Validate(payload);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == "WarehouseReceipt.OrderId");
            Assert.Contains(result.Errors, e => e.PropertyName == "WarehouseReceipt.WarehouseId");
            Assert.Contains(result.Errors, e => e.PropertyName == "WarehouseReceipt.DelivererName");
        }

        [Fact]
        public void UpdateMeasurements_WithValidData_ShouldPass()
        {
            var payload = new UpdateMeasurementsPayload
            {
                WarehouseReceipt = new UpdateMeasurementsBlock
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
                }
            };

            var result = _measurementsValidator.Validate(payload);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void UpdateMeasurements_WithNullBlock_ShouldFail()
        {
            var payload = new UpdateMeasurementsPayload
            {
                WarehouseReceipt = null!
            };

            var result = _measurementsValidator.Validate(payload);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == "WarehouseReceipt" && e.ErrorMessage.Contains("required"));
        }

        [Fact]
        public void UpdateMeasurements_WithInvalidItem_ShouldFail()
        {
            var payload = new UpdateMeasurementsPayload
            {
                WarehouseReceipt = new UpdateMeasurementsBlock
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
                }
            };

            var result = _measurementsValidator.Validate(payload);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].ItemName"));
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].ActualQty"));
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].CountryOfOrigin"));
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Items[0].ProductCategory"));
        }
    }
}
