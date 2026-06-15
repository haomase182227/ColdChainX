using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;

namespace ColdChainX.Infrastructure.Repositories
{
    public class WarehouseAttachmentRepository : IWarehouseAttachmentRepository
    {
        private readonly ApplicationDbContext _db;

        public WarehouseAttachmentRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<WarehouseEvidenceAttachment?> GetByIdAsync(Guid attachmentId)
        {
            return await _db.WarehouseEvidenceAttachments
                .Include(a => a.WarehouseReceipt)
                .Include(a => a.WarehouseReceiptItem)
                .Include(a => a.InventoryAdjustment)
                .Include(a => a.OutboundOrder)
                .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId);
        }

        public async Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptIdAsync(Guid receiptId)
        {
            return await _db.WarehouseEvidenceAttachments
                .Where(a => a.WarehouseReceiptId == receiptId)
                .ToListAsync();
        }

        public async Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdAsync(Guid receiptItemId)
        {
            return await _db.WarehouseEvidenceAttachments
                .Where(a => a.WarehouseReceiptItemId == receiptItemId)
                .ToListAsync();
        }

        public async Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdsAsync(IEnumerable<Guid> receiptItemIds)
        {
            if (receiptItemIds == null || !receiptItemIds.Any())
                return new List<WarehouseEvidenceAttachment>();

            return await _db.WarehouseEvidenceAttachments
                .Where(a => a.WarehouseReceiptItemId.HasValue && receiptItemIds.Contains(a.WarehouseReceiptItemId.Value))
                .ToListAsync();
        }

        public async Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByAdjustmentIdAsync(Guid adjustmentId)
        {
            return await _db.WarehouseEvidenceAttachments
                .Where(a => a.InventoryAdjustmentId == adjustmentId)
                .ToListAsync();
        }

        public async Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByOutboundOrderIdAsync(Guid outboundOrderId)
        {
            return await _db.WarehouseEvidenceAttachments
                .Where(a => a.OutboundOrderId == outboundOrderId)
                .ToListAsync();
        }

        public async Task AddAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            await _db.WarehouseEvidenceAttachments.AddAsync(attachment);
        }

        public async Task UpdateAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            _db.WarehouseEvidenceAttachments.Update(attachment);
            await Task.CompletedTask;
        }

        public async Task DeleteAttachmentAsync(WarehouseEvidenceAttachment attachment)
        {
            _db.WarehouseEvidenceAttachments.Remove(attachment);
            await Task.CompletedTask;
        }

        public async Task AddAuditHistoryAsync(AttachmentAuditHistory history)
        {
            await _db.AttachmentAuditHistories.AddAsync(history);
        }

        public async Task<List<AttachmentAuditHistory>> GetAuditHistoryByAttachmentIdAsync(Guid attachmentId)
        {
            return await _db.AttachmentAuditHistories
                .Where(h => h.AttachmentId == attachmentId)
                .OrderByDescending(h => h.ChangedAt)
                .ToListAsync();
        }

        public async Task<List<ComplianceZoningRule>> GetComplianceZoningRulesAsync()
        {
            return await _db.ComplianceZoningRules
                .Where(r => r.IsActive)
                .ToListAsync();
        }

        public async Task<List<ComplianceZoningRule>> GetComplianceZoningRulesByCategoryAsync(ProductCategory category)
        {
            return await _db.ComplianceZoningRules
                .Where(r => r.ProductCategory == category && r.IsActive)
                .ToListAsync();
        }

        public async Task AddComplianceZoningRuleAsync(ComplianceZoningRule rule)
        {
            await _db.ComplianceZoningRules.AddAsync(rule);
        }

        public async Task UpdateComplianceZoningRuleAsync(ComplianceZoningRule rule)
        {
            _db.ComplianceZoningRules.Update(rule);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
