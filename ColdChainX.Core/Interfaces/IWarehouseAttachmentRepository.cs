using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Interfaces
{
    public interface IWarehouseAttachmentRepository
    {
        Task<WarehouseEvidenceAttachment?> GetByIdAsync(Guid attachmentId);
        Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptIdAsync(Guid receiptId);
        Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdAsync(Guid receiptItemId);
        Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByReceiptItemIdsAsync(IEnumerable<Guid> receiptItemIds);
        Task<List<WarehouseEvidenceAttachment>> GetAttachmentsByOutboundOrderIdAsync(Guid outboundOrderId);
        Task<OutboundOrder?> GetOutboundOrderWithItemsAsync(Guid outboundOrderId);
        Task AddAttachmentAsync(WarehouseEvidenceAttachment attachment);
        Task UpdateAttachmentAsync(WarehouseEvidenceAttachment attachment);
        Task DeleteAttachmentAsync(WarehouseEvidenceAttachment attachment);

        Task AddAuditHistoryAsync(AttachmentAuditHistory history);
        Task<List<AttachmentAuditHistory>> GetAuditHistoryByAttachmentIdAsync(Guid attachmentId);

        Task<List<ComplianceZoningRule>> GetComplianceZoningRulesAsync();
        Task<List<ComplianceZoningRule>> GetComplianceZoningRulesByCategoryAsync(ProductCategory category);
        Task AddComplianceZoningRuleAsync(ComplianceZoningRule rule);
        Task UpdateComplianceZoningRuleAsync(ComplianceZoningRule rule);

        Task SaveChangesAsync();
    }
}
