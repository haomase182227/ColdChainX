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

        private WarehouseReceipt CreateBaseReceipt(ProductCategory category, string countryOfOrigin = "Vietnam", DateOnly? mfgDate = null, DateOnly? expDate = null)
        {
            var receiptId = Guid.NewGuid();
            var item = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ReceiptId = receiptId,
                ItemName = "Test Cargo Item",
                ProductCategory = category,
                CountryOfOrigin = countryOfOrigin,
                ManufacturedDate = mfgDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                ExpiryDate = expDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(180)),
                BatchNumber = "LOT-12345"
            };

            var receipt = new WarehouseReceipt
            {
                ReceiptId = receiptId,
                ReceiptCode = "REC-2026-0001",
                WarehouseReceiptItems = new List<WarehouseReceiptItem> { item }
            };

            return receipt;
        }

        private WarehouseEvidenceAttachment CreateAttachment(Guid receiptId, AttachmentSubCategory subCategory, DocumentStatus status, string? sealNumber = null)
        {
            return new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                WarehouseReceiptId = receiptId,
                SubCategory = subCategory,
                Status = status,
                SealNumber = sealNumber,
                FileName = $"{subCategory}.png",
                FilePath = $"/uploads/{subCategory}.png",
                ContentType = "image/png",
                Format = AttachmentFormat.IMAGE,
                Category = AttachmentCategory.COMPLIANCE
            };
        }

        [Fact]
        public void Food_Missing_ExpiryDate_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.FOOD, countryOfOrigin: "Vietnam");
            foreach (var item in receipt.WarehouseReceiptItems)
            {
                item.ExpiryDate = null;
            }
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains("ExpiryDate"));
        }

        [Fact]
        public void Seafood_Missing_QuarantineCertificate_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.SEAFOOD);
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.QUARANTINE_CERTIFICATE.ToString()));
        }

        [Fact]
        public void Pharma_Missing_Coa_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.PHARMA);
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.COA_CERTIFICATE.ToString()));
        }

        [Fact]
        public void Vaccine_Missing_ProductLicense_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.VACCINE);
            
            // Add COA but omit Product License
            var coa = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.COA_CERTIFICATE, DocumentStatus.VERIFIED);
            var attachments = new List<WarehouseEvidenceAttachment> { coa };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.PRODUCT_LICENSE.ToString()));
            Assert.DoesNotContain(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.COA_CERTIFICATE.ToString()));
        }

        [Fact]
        public void ImportGoods_Missing_CustomsDeclaration_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.IMPORT_GOODS);
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.CUSTOMS_DECLARATION.ToString()));
        }

        [Fact]
        public void ImportGoods_Missing_CertificateOfOrigin_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.IMPORT_GOODS);
            
            // Add Customs Declaration but omit Certificate of Origin
            var customs = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.CUSTOMS_DECLARATION, DocumentStatus.VERIFIED);
            var attachments = new List<WarehouseEvidenceAttachment> { customs };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.CERTIFICATE_OF_ORIGIN.ToString()));
            Assert.DoesNotContain(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.CUSTOMS_DECLARATION.ToString()));
        }

        [Fact]
        public void ImportGoods_Missing_SealPhoto_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.IMPORT_GOODS);
            
            // Add Customs and CO but omit Seal Photo
            var customs = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.CUSTOMS_DECLARATION, DocumentStatus.VERIFIED);
            var co = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.CERTIFICATE_OF_ORIGIN, DocumentStatus.VERIFIED);
            var attachments = new List<WarehouseEvidenceAttachment> { customs, co };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.SEAL_PHOTO.ToString()));
        }

        [Fact]
        public void Rejected_Attachment_AddsToFailedRequirements()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.SEAFOOD);
            
            // Add rejected Quarantine Certificate
            var quarantine = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.QUARANTINE_CERTIFICATE, DocumentStatus.REJECTED);
            var attachments = new List<WarehouseEvidenceAttachment> { quarantine };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.FailedRequirements, r => r.Contains(AttachmentSubCategory.QUARANTINE_CERTIFICATE.ToString()));
            Assert.Empty(result.MissingRequirements); // Exists but rejected, so not missing
        }

        [Fact]
        public void Pending_Attachment_AddsToPendingRequirements()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.SEAFOOD);
            
            // Add pending Quarantine Certificate
            var quarantine = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.QUARANTINE_CERTIFICATE, DocumentStatus.PENDING);
            var attachments = new List<WarehouseEvidenceAttachment> { quarantine };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.PendingRequirements, r => r.Contains(AttachmentSubCategory.QUARANTINE_CERTIFICATE.ToString()));
            Assert.Empty(result.MissingRequirements); // Exists but pending, so not missing
        }

        [Fact]
        public void Successful_Validation_Passes()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.VACCINE);
            
            var coa = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.COA_CERTIFICATE, DocumentStatus.VERIFIED);
            var license = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.PRODUCT_LICENSE, DocumentStatus.VERIFIED);
            var attachments = new List<WarehouseEvidenceAttachment> { coa, license };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.True(result.Passed);
            Assert.Empty(result.MissingRequirements);
            Assert.Empty(result.FailedRequirements);
            Assert.Empty(result.PendingRequirements);
        }

        [Fact]
        public void Vietnam_Origin_DoesNotTrigger_ImportRules()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.FOOD, countryOfOrigin: "Vietnam");
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.True(result.Passed); // No attachments required for domestic FOOD category
            Assert.Empty(result.MissingRequirements);
        }

        [Fact]
        public void NonVietnam_Origin_Triggers_ImportRules()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.FOOD, countryOfOrigin: "Norway");
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed); // Triggers import rules and fails due to missing import docs
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.CUSTOMS_DECLARATION.ToString()));
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.CERTIFICATE_OF_ORIGIN.ToString()));
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.SEAL_PHOTO.ToString()));
        }

        [Fact]
        public void SealPhoto_WithEmptySealNumber_FailsValidation()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.IMPORT_GOODS);
            
            var customs = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.CUSTOMS_DECLARATION, DocumentStatus.VERIFIED);
            var co = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.CERTIFICATE_OF_ORIGIN, DocumentStatus.VERIFIED);
            // Seal photo verified but with empty seal number
            var seal = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.SEAL_PHOTO, DocumentStatus.VERIFIED, sealNumber: "");
            
            var attachments = new List<WarehouseEvidenceAttachment> { customs, co, seal };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.FailedRequirements, r => r.Contains("SEAL_PHOTO verified but is missing a valid SealNumber"));
        }

        [Fact]
        public void AttachmentPrecedence_PendingOverridesRejected()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.SEAFOOD);
            
            // One rejected, one pending quarantine certificate
            var rejectedQuarantine = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.QUARANTINE_CERTIFICATE, DocumentStatus.REJECTED);
            var pendingQuarantine = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.QUARANTINE_CERTIFICATE, DocumentStatus.PENDING);
            var attachments = new List<WarehouseEvidenceAttachment> { rejectedQuarantine, pendingQuarantine };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.PendingRequirements, r => r.Contains(AttachmentSubCategory.QUARANTINE_CERTIFICATE.ToString()));
            Assert.Empty(result.FailedRequirements); // PENDING overrides REJECTED, not failed.
        }

        [Fact]
        public void AttachmentPrecedence_OnlyFailsWhenAllRejected()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.SEAFOOD);
            
            // One rejected, one expired quarantine certificate
            var rejectedQuarantine = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.QUARANTINE_CERTIFICATE, DocumentStatus.REJECTED);
            var expiredQuarantine = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.QUARANTINE_CERTIFICATE, DocumentStatus.EXPIRED);
            var attachments = new List<WarehouseEvidenceAttachment> { rejectedQuarantine, expiredQuarantine };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.MissingRequirements, r => r.Contains(AttachmentSubCategory.QUARANTINE_CERTIFICATE.ToString()));
            Assert.Empty(result.FailedRequirements); // Not all rejected (one is EXPIRED), so not failed.
        }

        [Fact]
        public void SealPhoto_PendingOverridesVerifiedWithoutSealNumber()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.IMPORT_GOODS);
            
            var customs = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.CUSTOMS_DECLARATION, DocumentStatus.VERIFIED);
            var co = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.CERTIFICATE_OF_ORIGIN, DocumentStatus.VERIFIED);
            // One verified but missing seal number, one pending
            var invalidVerified = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.SEAL_PHOTO, DocumentStatus.VERIFIED, sealNumber: "");
            var pending = CreateAttachment(receipt.ReceiptId, AttachmentSubCategory.SEAL_PHOTO, DocumentStatus.PENDING);
            
            var attachments = new List<WarehouseEvidenceAttachment> { customs, co, invalidVerified, pending };

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.False(result.Passed);
            Assert.Contains(result.PendingRequirements, r => r.Contains("SEAL_PHOTO is PENDING verification"));
            Assert.Empty(result.FailedRequirements);
        }

        [Fact]
        public void CountryOfOrigin_vietnam_Spaced_IsDomestic()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.FOOD, countryOfOrigin: " vietnam ");
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.True(result.Passed);
            Assert.Empty(result.MissingRequirements);
        }

        [Fact]
        public void CountryOfOrigin_VIETNAM_Uppercase_IsDomestic()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.FOOD, countryOfOrigin: "VIETNAM");
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.True(result.Passed);
            Assert.Empty(result.MissingRequirements);
        }

        [Fact]
        public void CountryOfOrigin_Whitespace_IsDomestic()
        {
            // Arrange
            var receipt = CreateBaseReceipt(ProductCategory.FOOD, countryOfOrigin: "   ");
            var attachments = new List<WarehouseEvidenceAttachment>();

            // Act
            var result = _engine.ValidateReceipt(receipt, attachments);

            // Assert
            Assert.True(result.Passed);
            Assert.Empty(result.MissingRequirements);
        }
    }
}
