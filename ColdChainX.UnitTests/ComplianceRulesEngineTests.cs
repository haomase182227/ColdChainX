using System;
using System.Collections.Generic;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Application.Models;
using ColdChainX.Application.Services;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class ComplianceRulesEngineTests
    {
        private readonly ComplianceRulesEngine _engine;

        public ComplianceRulesEngineTests()
        {
            _engine = new ComplianceRulesEngine();
        }

        [Fact]
        public void ValidateReceipt_NullReceipt_Fails()
        {
            var result = _engine.ValidateReceipt(null!);
            Assert.False(result.Passed);
            Assert.Contains(result.FailedRequirements, r => r.Contains("Receipt cannot be null."));
        }

        [Fact]
        public void ValidateReceipt_ValidReceipt_Passes()
        {
            var receipt = new WarehouseReceipt();
            var result = _engine.ValidateReceipt(receipt);
            Assert.True(result.Passed);
        }

        [Fact]
        public void ValidateOutboundOrder_NullOrder_Fails()
        {
            var result = _engine.ValidateOutboundOrder(null!);
            Assert.False(result.Passed);
            Assert.Contains(result.FailedRequirements, r => r.Contains("Order cannot be null."));
        }

        [Fact]
        public void ValidateOutboundOrder_ValidOrder_Passes()
        {
            var order = new OutboundOrder();
            var result = _engine.ValidateOutboundOrder(order);
            Assert.True(result.Passed);
        }
    }
}
