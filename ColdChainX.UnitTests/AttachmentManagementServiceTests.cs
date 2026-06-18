using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Attachment;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Models;
using ColdChainX.Application.Services;
using ColdChainX.Application.Validators;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class AttachmentManagementServiceTests
    {
        private readonly MockAttachmentRepository _attachmentRepo;
        private readonly MockReceiptRepository _receiptRepo;
        private readonly MockFileService _fileService;
        private readonly UploadAttachmentRequestValidator _uploadValidator;
        private readonly VerifyAttachmentRequestValidator _verifyValidator;
        private readonly AttachmentManagementService _service;

        public AttachmentManagementServiceTests()
        {
            _attachmentRepo = new MockAttachmentRepository();
            _receiptRepo = new MockReceiptRepository();
            _fileService = new MockFileService();
            _uploadValidator = new UploadAttachmentRequestValidator();
            _verifyValidator = new VerifyAttachmentRequestValidator();

            _service = new AttachmentManagementService(
                _attachmentRepo,
                _receiptRepo,
                _fileService,
                _uploadValidator,
                _verifyValidator
            );
        }

        private IFormFile CreateFormFile(string fileName, string contentType, string content = "dummy file content")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        [Fact]
        public async Task UploadAttachment_Success_StoresMetadata_And_CreatesAuditLog()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var file = CreateFormFile("customs_declaration.pdf", "application/pdf");
            
            var request = new UploadAttachmentRequest
            {
                File = file,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION,
                WarehouseReceiptId = receiptId,
                DocumentNumber = "CD-9988",
                Issuer = "Customs Dept",
                IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
            };

            // Act
            var result = await _service.UploadAttachmentAsync(request, userId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("customs_declaration.pdf", result.Data.FileName);
            Assert.Equal("PENDING", result.Data.Status);
            Assert.Equal(receiptId, result.Data.WarehouseReceiptId);
            Assert.Equal("CD-9988", result.Data.DocumentNumber);

            // Verify Repository
            Assert.Single(_attachmentRepo.Attachments);
            var saved = _attachmentRepo.Attachments.First();
            Assert.Equal(DocumentStatus.PENDING, saved.Status);
            Assert.Equal($"/uploads/customs_declaration.pdf", saved.FilePath);

            // Verify Audit Log
            Assert.Single(_attachmentRepo.AuditHistories);
            var audit = _attachmentRepo.AuditHistories.First();
            Assert.Null(audit.PreviousStatus);
            Assert.Equal(DocumentStatus.PENDING, audit.NewStatus);
            Assert.Equal(userId, audit.ChangedBy);
            Assert.Equal("Attachment uploaded.", audit.Reason);
        }

        [Fact]
        public async Task UploadAttachment_InvalidRequest_ReturnsFailure()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var file = CreateFormFile("test.exe", "application/octet-stream"); // Invalid extension
            var request = new UploadAttachmentRequest
            {
                File = file,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION
                // Missing target ID (receiptId, etc.)
            };

            // Act
            var result = await _service.UploadAttachmentAsync(request, userId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Unsupported file format", result.Message);
            Assert.Contains("linked to exactly one target", result.Message);
            Assert.Empty(_attachmentRepo.Attachments);
            Assert.Empty(_attachmentRepo.AuditHistories);
        }

        [Fact]
        public async Task VerifyAttachment_Approve_UpdatesStatus_And_CreatesAuditLog_And_EvaluatesCompliance()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            
            // Seed Receipt
            var receipt = new WarehouseReceipt
            {
                ReceiptId = receiptId,
                ReceiptCode = "REC-101",
                WarehouseReceiptItems = new List<WarehouseReceiptItem>
                {
                    new WarehouseReceiptItem
                    {
                        ItemId = Guid.NewGuid(),
                        ReceiptId = receiptId,
                        ItemName = "Food Cargo Item",
                        ProductCategory = ProductCategory.FOOD,
                        CountryOfOrigin = "Vietnam",
                        ManufacturedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(50))
                    }
                }
            };
            await _receiptRepo.AddAsync(receipt);

            // Seed Attachment
            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                FileName = "customs.pdf",
                FilePath = "/uploads/customs.pdf",
                Status = DocumentStatus.PENDING,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION,
                WarehouseReceiptId = receiptId
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.VERIFIED
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachment.AttachmentId, request, userId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.Passed); // Food with Vietnam origin and valid dates has no remaining requirements

            // Verify Repository Update
            var updated = await _attachmentRepo.GetByIdAsync(attachment.AttachmentId);
            Assert.NotNull(updated);
            Assert.Equal(DocumentStatus.VERIFIED, updated.Status);
            Assert.Equal(userId, updated.VerifiedBy);
            Assert.NotNull(updated.VerifiedAt);

            // Verify Audit History
            Assert.Single(_attachmentRepo.AuditHistories);
            var audit = _attachmentRepo.AuditHistories.First();
            Assert.Equal(DocumentStatus.PENDING, audit.PreviousStatus);
            Assert.Equal(DocumentStatus.VERIFIED, audit.NewStatus);
            Assert.Equal("Attachment approved.", audit.Reason);
        }

        [Fact]
        public async Task VerifyAttachment_Reject_EnforcesRejectionReason_And_CreatesAuditLog()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();

            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = attachmentId,
                FileName = "license.pdf",
                FilePath = "/uploads/license.pdf",
                Status = DocumentStatus.PENDING,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.PRODUCT_LICENSE
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.REJECTED,
                RejectionReason = "Illegible watermark"
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachmentId, request, userId);

            // Assert
            Assert.True(result.Success);

            var updated = await _attachmentRepo.GetByIdAsync(attachmentId);
            Assert.NotNull(updated);
            Assert.Equal(DocumentStatus.REJECTED, updated.Status);
            Assert.Equal("Illegible watermark", updated.RejectionReason);

            Assert.Single(_attachmentRepo.AuditHistories);
            var audit = _attachmentRepo.AuditHistories.First();
            Assert.Equal(DocumentStatus.PENDING, audit.PreviousStatus);
            Assert.Equal(DocumentStatus.REJECTED, audit.NewStatus);
            Assert.Equal("Illegible watermark", audit.Reason);
        }

        [Fact]
        public async Task VerifyAttachment_Reject_WithoutReason_ReturnsFailure()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();

            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = attachmentId,
                FileName = "license.pdf",
                FilePath = "/uploads/license.pdf",
                Status = DocumentStatus.PENDING,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.PRODUCT_LICENSE
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.REJECTED,
                RejectionReason = "" // Invalid empty rejection reason
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachmentId, request, userId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Rejection reason is required", result.Message);

            var current = await _attachmentRepo.GetByIdAsync(attachmentId);
            Assert.NotNull(current);
            Assert.Equal(DocumentStatus.PENDING, current.Status); // Status unchanged
        }

        [Fact]
        public async Task VerifyAttachment_NonReceiptTarget_DoesNotRunCompliance()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();

            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = attachmentId,
                FileName = "adjustment_photo.png",
                FilePath = "/uploads/adjustment_photo.png",
                Status = DocumentStatus.PENDING,
                Category = AttachmentCategory.EVIDENCE,
                SubCategory = AttachmentSubCategory.DAMAGE_PHOTO,
                InventoryAdjustmentId = Guid.NewGuid() // Linked to adjustment, not receipt
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.VERIFIED
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachmentId, request, userId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.Passed); // Returns default Passed=true result without executing engine
        }

        [Fact]
        public async Task VerifiedToRejected_ShouldFail()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();
            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = attachmentId,
                FileName = "doc.pdf",
                FilePath = "/uploads/doc.pdf",
                Status = DocumentStatus.VERIFIED, // Currently VERIFIED
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.REJECTED,
                RejectionReason = "Actually invalid document."
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachmentId, request, userId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Cannot transition attachment from VERIFIED to REJECTED", result.Message);
            
            var current = await _attachmentRepo.GetByIdAsync(attachmentId);
            Assert.NotNull(current);
            Assert.Equal(DocumentStatus.VERIFIED, current.Status); // Status remains VERIFIED
        }

        [Fact]
        public async Task RejectedToVerified_ShouldFail()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();
            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = attachmentId,
                FileName = "doc.pdf",
                FilePath = "/uploads/doc.pdf",
                Status = DocumentStatus.REJECTED, // Currently REJECTED
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION,
                RejectionReason = "Illegible"
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.VERIFIED
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachmentId, request, userId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Cannot transition attachment from REJECTED to VERIFIED", result.Message);
            
            var current = await _attachmentRepo.GetByIdAsync(attachmentId);
            Assert.NotNull(current);
            Assert.Equal(DocumentStatus.REJECTED, current.Status); // Status remains REJECTED
        }

        [Fact]
        public async Task VerifiedToPending_ShouldFail()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();
            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = attachmentId,
                FileName = "doc.pdf",
                FilePath = "/uploads/doc.pdf",
                Status = DocumentStatus.VERIFIED, // Currently VERIFIED
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.PENDING
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachmentId, request, userId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Cannot transition attachment from VERIFIED to PENDING", result.Message);
            
            var current = await _attachmentRepo.GetByIdAsync(attachmentId);
            Assert.NotNull(current);
            Assert.Equal(DocumentStatus.VERIFIED, current.Status); // Status remains VERIFIED
        }

        [Fact]
        public async Task RejectedToPending_ShouldFail()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var attachmentId = Guid.NewGuid();
            var attachment = new WarehouseEvidenceAttachment
            {
                AttachmentId = attachmentId,
                FileName = "doc.pdf",
                FilePath = "/uploads/doc.pdf",
                Status = DocumentStatus.REJECTED, // Currently REJECTED
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION,
                RejectionReason = "Bad photo"
            };
            await _attachmentRepo.AddAttachmentAsync(attachment);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.PENDING
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(attachmentId, request, userId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Cannot transition attachment from REJECTED to PENDING", result.Message);
            
            var current = await _attachmentRepo.GetByIdAsync(attachmentId);
            Assert.NotNull(current);
            Assert.Equal(DocumentStatus.REJECTED, current.Status); // Status remains REJECTED
        }

        [Fact]
        public async Task VerifyAttachment_OutboundOrder_RunsOutboundCompliance()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            // Seed Outbound Order
            var order = new OutboundOrder
            {
                OutboundOrderId = orderId,
                OrderCode = "OUT-101",
                OutboundOrderItems = new List<OutboundOrderItem>
                {
                    new OutboundOrderItem
                    {
                        OutboundOrderItemId = Guid.NewGuid(),
                        OutboundOrderId = orderId,
                        ItemCode = "ITEM-FOOD",
                        ItemName = "Food Item",
                        Quantity = 5
                    }
                }
            };
            _attachmentRepo.OutboundOrders.Add(order);

            // Seed Receipt Item to resolve product category
            var receiptItem = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ItemCode = "ITEM-FOOD",
                ItemName = "Food Item",
                ProductCategory = ProductCategory.FOOD
            };
            _receiptRepo.ReceiptItems.Add(receiptItem);

            // Seed Attachments
            // Need: WAREHOUSE_ISSUE_NOTE, GOODS_CONDITION_PHOTO, and (TEMPERATURE_PHOTO or TEMPERATURE_LOG)
            var issueNote = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                FileName = "issue_note.pdf",
                FilePath = "/uploads/issue_note.pdf",
                Status = DocumentStatus.VERIFIED,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.WAREHOUSE_ISSUE_NOTE,
                OutboundOrderId = orderId
            };
            var conditionPhoto = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                FileName = "condition.png",
                FilePath = "/uploads/condition.png",
                Status = DocumentStatus.VERIFIED,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.GOODS_CONDITION_PHOTO,
                OutboundOrderId = orderId
            };
            var tempPhoto = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                FileName = "temp.png",
                FilePath = "/uploads/temp.png",
                Status = DocumentStatus.VERIFIED,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.TEMPERATURE_PHOTO,
                OutboundOrderId = orderId
            };

            await _attachmentRepo.AddAttachmentAsync(issueNote);
            await _attachmentRepo.AddAttachmentAsync(conditionPhoto);
            await _attachmentRepo.AddAttachmentAsync(tempPhoto);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.VERIFIED
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(tempPhoto.AttachmentId, request, userId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.Passed);
        }

        [Fact]
        public async Task VerifyAttachment_OutboundOrder_AmbiguitySafeguard_FailsValidation()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            // Seed Outbound Order
            var order = new OutboundOrder
            {
                OutboundOrderId = orderId,
                OrderCode = "OUT-102",
                OutboundOrderItems = new List<OutboundOrderItem>
                {
                    new OutboundOrderItem
                    {
                        OutboundOrderItemId = Guid.NewGuid(),
                        OutboundOrderId = orderId,
                        ItemCode = "ITEM-AMBIGUOUS",
                        ItemName = "Ambiguous Item",
                        Quantity = 5
                    }
                }
            };
            _attachmentRepo.OutboundOrders.Add(order);

            // Seed two different receipt items for same ItemCode with different categories
            var item1 = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ItemCode = "ITEM-AMBIGUOUS",
                ItemName = "Ambiguous Item",
                ProductCategory = ProductCategory.FOOD
            };
            var item2 = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ItemCode = "ITEM-AMBIGUOUS",
                ItemName = "Ambiguous Item",
                ProductCategory = ProductCategory.PHARMA
            };
            _receiptRepo.ReceiptItems.Add(item1);
            _receiptRepo.ReceiptItems.Add(item2);

            var issueNote = new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                FileName = "issue_note.pdf",
                FilePath = "/uploads/issue_note.pdf",
                Status = DocumentStatus.PENDING,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.WAREHOUSE_ISSUE_NOTE,
                OutboundOrderId = orderId
            };
            await _attachmentRepo.AddAttachmentAsync(issueNote);

            var request = new VerifyAttachmentRequest
            {
                Status = DocumentStatus.VERIFIED
            };

            // Act
            var result = await _service.VerifyAttachmentAsync(issueNote.AttachmentId, request, userId);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.False(result.Data.Passed);
            Assert.Contains(result.Data.FailedRequirements, r => r.Contains("Ambiguous product category"));
        }

        [Fact]
        public async Task UploadAttachment_FileExceeds10MB_ValidationFails()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var content11Mb = new string('A', 11 * 1024 * 1024);
            var file = CreateFormFile("huge_report.pdf", "application/pdf", content11Mb);
            
            var request = new UploadAttachmentRequest
            {
                File = file,
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION,
                WarehouseReceiptId = receiptId,
                DocumentNumber = "CD-9988",
                Issuer = "Customs Dept",
                IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
            };

            // Act
            var result = await _service.UploadAttachmentAsync(request, userId);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("File size must not exceed 10 MB", result.Message);
        }
    }

    #region Mock Classes

    public class MockAttachmentRepository : IWarehouseAttachmentRepository
    {
        public List<WarehouseEvidenceAttachment> Attachments { get; } = new();
        public List<AttachmentAuditHistory> AuditHistories { get; } = new();
        public List<ComplianceZoningRule> ComplianceRules { get; } = new();

        public Task<WarehouseEvidenceAttachment?> GetByIdAsync(Guid attachmentId)
        {
            var att = Attachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
            return Task.FromResult(att);
        }

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptIdAsync(Guid receiptId)
        {
            var list = Attachments.Where(a => a.WarehouseReceiptId == receiptId).ToList();
            return Task.FromResult(list);
        }

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdAsync(Guid receiptItemId)
        {
            var list = Attachments.Where(a => a.WarehouseReceiptItemId == receiptItemId).ToList();
            return Task.FromResult(list);
        }

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdsAsync(IEnumerable<Guid> receiptItemIds)
        {
            var list = Attachments.Where(a => a.WarehouseReceiptItemId.HasValue && receiptItemIds.Contains(a.WarehouseReceiptItemId.Value)).ToList();
            return Task.FromResult(list);
        }

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByAdjustmentIdAsync(Guid adjustmentId)
        {
            var list = Attachments.Where(a => a.InventoryAdjustmentId == adjustmentId).ToList();
            return Task.FromResult(list);
        }

        public Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByOutboundOrderIdAsync(Guid outboundOrderId)
        {
            var list = Attachments.Where(a => a.OutboundOrderId == outboundOrderId).ToList();
            return Task.FromResult(list);
        }

        public List<OutboundOrder> OutboundOrders { get; } = new();

        public Task<OutboundOrder?> GetOutboundOrderWithItemsAsync(Guid outboundOrderId)
        {
            return Task.FromResult(OutboundOrders.FirstOrDefault(o => o.OutboundOrderId == outboundOrderId));
        }

        public Task AddAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            Attachments.Add(attachment);
            return Task.CompletedTask;
        }

        public Task UpdateAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            var existing = Attachments.FirstOrDefault(a => a.AttachmentId == attachment.AttachmentId);
            if (existing != null)
            {
                Attachments.Remove(existing);
            }
            Attachments.Add(attachment);
            return Task.CompletedTask;
        }

        public Task DeleteAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            Attachments.Remove(attachment);
            return Task.CompletedTask;
        }

        public Task AddAuditHistoryAsync(AttachmentAuditHistory history)
        {
            AuditHistories.Add(history);
            return Task.CompletedTask;
        }

        public Task<List<AttachmentAuditHistory>> GetAuditHistoryByAttachmentIdAsync(Guid attachmentId)
        {
            var list = AuditHistories.Where(h => h.AttachmentId == attachmentId).OrderByDescending(h => h.ChangedAt).ToList();
            return Task.FromResult(list);
        }

        public Task<List<ComplianceZoningRule>> GetComplianceZoningRulesAsync()
        {
            return Task.FromResult(ComplianceRules);
        }

        public Task<List<ComplianceZoningRule>> GetComplianceZoningRulesByCategoryAsync(ProductCategory category)
        {
            return Task.FromResult(ComplianceRules.Where(r => r.ProductCategory == category).ToList());
        }

        public Task AddComplianceZoningRuleAsync(ComplianceZoningRule rule)
        {
            ComplianceRules.Add(rule);
            return Task.CompletedTask;
        }

        public Task UpdateComplianceZoningRuleAsync(ComplianceZoningRule rule)
        {
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            return Task.CompletedTask;
        }
    }

    public class MockReceiptRepository : IWarehouseReceiptRepository
    {
        public List<WarehouseReceipt> Receipts { get; } = new();
        public List<WarehouseReceiptItem> ReceiptItems { get; } = new();

        public Task<WarehouseReceipt?> GetByIdAsync(Guid receiptId)
        {
            return Task.FromResult(Receipts.FirstOrDefault(r => r.ReceiptId == receiptId));
        }

        public Task<WarehouseReceipt?> GetByOrderIdAsync(Guid orderId)
        {
            return Task.FromResult(Receipts.FirstOrDefault(r => r.OrderId == orderId));
        }

        public Task<List<WarehouseReceipt>> GetActiveReceiptsByWarehouseIdAsync(Guid warehouseId)
        {
            return Task.FromResult(Receipts.Where(r => r.WarehouseId == warehouseId).ToList());
        }

        public Task<List<WarehouseReceiptItem>> GetReceiptItemsByItemCodesAsync(IEnumerable<string> itemCodes)
        {
            var list = ReceiptItems.Where(i => i.ItemCode != null && itemCodes.Contains(i.ItemCode)).ToList();
            return Task.FromResult(list);
        }

        public Task AddAsync(WarehouseReceipt receipt)
        {
            Receipts.Add(receipt);
            return Task.CompletedTask;
        }

        public Task AddItemAsync(WarehouseReceiptItem item)
        {
            ReceiptItems.Add(item);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            return Task.CompletedTask;
        }
    }

    public class MockFileService : IFileService
    {
        public Task<string> UploadFileAsync(IFormFile file)
        {
            return Task.FromResult($"/uploads/{file.FileName}");
        }

        public Task<string> UploadFileAsync(Stream stream, string fileName)
        {
            return Task.FromResult($"/uploads/{fileName}");
        }

        public Task<string> UploadFileAsync(byte[] fileBytes, string fileName)
        {
            return Task.FromResult($"/uploads/{fileName}");
        }
    }

    #endregion
}
