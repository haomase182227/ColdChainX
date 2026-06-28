using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Features.Inventory.Queries;

public class GetLpnDocumentsQuery : IRequest<List<LpnDocumentDto>>
{
    public Guid LpnId { get; set; }
    public GetLpnDocumentsQuery(Guid lpnId) => LpnId = lpnId;
}

public class LpnDocumentDto
{
    public string DocumentType { get; set; } = null!;
    public string DocumentName { get; set; } = null!;
    public string Url { get; set; } = null!;
}

public class GetLpnDocumentsQueryHandler : IRequestHandler<GetLpnDocumentsQuery, List<LpnDocumentDto>?>
{
    private readonly IApplicationDbContext _context;

    public GetLpnDocumentsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<LpnDocumentDto>?> Handle(GetLpnDocumentsQuery request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns
            .Include(l => l.Receipt)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return null;

        var docs = new List<LpnDocumentDto>();

        // 1. Warehouse Receipt (Phiếu nhập kho)
        docs.Add(new LpnDocumentDto
        {
            DocumentType = "WarehouseReceipt",
            DocumentName = "Phiếu Nhập Kho",
            Url = $"/api/inbound/receipts/{lpn.ReceiptId}/pdf"
        });

        // 2. Discrepancy Note (Biên bản bất thường) - Always generated dynamically if requested
        if (!string.IsNullOrEmpty(lpn.DiscrepancyReason) || !string.IsNullOrEmpty(lpn.EvidenceImageUrl))
        {
            docs.Add(new LpnDocumentDto
            {
                DocumentType = "DiscrepancyNote",
                DocumentName = "Biên bản Bất thường",
                Url = $"/api/discrepancy/{lpn.LpnId}/pdf"
            });
        }

        // 3. Evidence Image (Hình ảnh bằng chứng)
        if (!string.IsNullOrEmpty(lpn.EvidenceImageUrl))
        {
            docs.Add(new LpnDocumentDto
            {
                DocumentType = "EvidenceImage",
                DocumentName = "Hình ảnh Bằng chứng QC",
                Url = lpn.EvidenceImageUrl
            });
        }

        // 4. Dispatch Note / Manifest (Phiếu xuất kho)
        if (lpn.TripId.HasValue)
        {
            docs.Add(new LpnDocumentDto
            {
                DocumentType = "DispatchNote",
                DocumentName = "Phiếu Xuất Kho kiêm Bảng Kê",
                Url = $"/api/dispatch/trips/{lpn.TripId.Value}/export-pdf"
            });
        }

        // 5. ePOD (Proof of Delivery for the Order)
        if (lpn.State == ColdChainX.Core.Enums.LpnState.RELEASED || 
            lpn.State == ColdChainX.Core.Enums.LpnState.SHIPPING ||
            lpn.State == ColdChainX.Core.Enums.LpnState.DELIVERED ||
            lpn.State == ColdChainX.Core.Enums.LpnState.RETURN_PENDING ||
            lpn.State == ColdChainX.Core.Enums.LpnState.DELIVERY_RETURNED)
        {
            docs.Add(new LpnDocumentDto
            {
                DocumentType = "ePOD",
                DocumentName = "Giấy báo phát (ePOD)",
                Url = $"/api/outbound/orders/{lpn.OrderId}/epod-pdf"
            });
        }

        return docs;
    }
}
