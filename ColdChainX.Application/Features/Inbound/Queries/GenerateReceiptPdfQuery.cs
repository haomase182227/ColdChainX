using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inbound.Queries;

public class GenerateReceiptPdfQuery : IRequest<byte[]>
{
    public Guid ReceiptId { get; set; }

    public GenerateReceiptPdfQuery(Guid receiptId)
    {
        ReceiptId = receiptId;
    }
}

public class GenerateReceiptPdfQueryHandler : IRequestHandler<GenerateReceiptPdfQuery, byte[]>
{
    private readonly IApplicationDbContext _context;
    private readonly IPdfGeneratorService _pdfGenerator;

    public GenerateReceiptPdfQueryHandler(IApplicationDbContext context, IPdfGeneratorService pdfGenerator)
    {
        _context = context;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<byte[]> Handle(GenerateReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await _context.WarehouseReceipts
            .Include(x => x.Lpns)
                .ThenInclude(l => l.Order)
            .Include(x => x.Order)
                .ThenInclude(o => o.Customer)
            .Include(x => x.Warehouse)
            .FirstOrDefaultAsync(x => x.ReceiptId == request.ReceiptId, cancellationToken);

        if (receipt == null)
            throw new Exception("Warehouse receipt not found");

        var data = new
        {
            CompanyName = "ColdChainX Logistics",
            HubName = receipt.Warehouse?.WarehouseName ?? "N/A",
            HubAddress = receipt.Warehouse?.Address ?? "N/A",
            CreatedDay = receipt.CreatedAt?.Day.ToString("00") ?? DateTime.Now.Day.ToString("00"),
            CreatedMonth = receipt.CreatedAt?.Month.ToString("00") ?? DateTime.Now.Month.ToString("00"),
            CreatedYear = receipt.CreatedAt?.Year.ToString() ?? DateTime.Now.Year.ToString(),
            ReceiptCode = receipt.ReceiptCode,
            DriverName = receipt.DelivererName,
            OrderCode = receipt.Order?.TrackingCode ?? "N/A",
            CustomerName = receipt.Order?.Customer?.CompanyName ?? "N/A",
            CreatorName = "", 
            WarehouseManagerName = "",
            Items = receipt.Lpns.Select((item, index) => new
            {
                Index = index + 1,
                ItemName = item.Order?.ItemName ?? "Unknown",
                LpnCode = item.LpnCode,
                Unit = item.Order?.PackingType ?? "N/A",
                ExpectedQty = item.Order?.Quantity ?? 0,
                ActualQty = item.Quantity,
                Notes = item.State.ToString()
            })
        };

        return await _pdfGenerator.GeneratePdfAsync("WarehouseReceipt", data);
    }
}
