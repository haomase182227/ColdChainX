using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Discrepancy.Queries;

public class GenerateDiscrepancyPdfQuery : IRequest<byte[]>
{
    public Guid ReceiptId { get; set; }

    public GenerateDiscrepancyPdfQuery(Guid receiptId)
    {
        ReceiptId = receiptId;
    }
}

public class GenerateDiscrepancyPdfQueryHandler : IRequestHandler<GenerateDiscrepancyPdfQuery, byte[]>
{
    private readonly IApplicationDbContext _context;
    private readonly IPdfGeneratorService _pdfGenerator;

    public GenerateDiscrepancyPdfQueryHandler(IApplicationDbContext context, IPdfGeneratorService pdfGenerator)
    {
        _context = context;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<byte[]> Handle(GenerateDiscrepancyPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await _context.WarehouseReceipts
            .Include(x => x.Lpns)
                .ThenInclude(l => l.Order)
            .Include(x => x.Order)
            .Include(x => x.Warehouse)
            .FirstOrDefaultAsync(x => x.ReceiptId == request.ReceiptId, cancellationToken);

        if (receipt == null)
            throw new Exception("Warehouse receipt not found");

        var discrepancyItems = receipt.Lpns
            .Where(x => x.State == ColdChainX.Core.Enums.LpnState.DISCREPANCY_HOLD || x.State == ColdChainX.Core.Enums.LpnState.RETURN_PENDING || x.Quantity != (x.Order?.Quantity ?? 0))
            .ToList();

        var data = new
        {
            CompanyName = "ColdChainX Logistics",
            HubName = receipt.Warehouse?.WarehouseName ?? "N/A",
            DiscrepancyCode = $"BBBT-{receipt.ReceiptCode}",
            Time = DateTime.Now.ToString("HH:mm"),
            Date = DateTime.Now.ToString("dd/MM/yyyy"),
            QcName = "",
            DriverName = receipt.DelivererName ?? "N/A",
            VehiclePlateNumber = "N/A", // Truck plate is usually on trip
            OrderCode = receipt.Order?.TrackingCode ?? "N/A",
            DiscrepancyItems = discrepancyItems.Select((item, index) => new
            {
                Index = index + 1,
                LpnCode = item.LpnCode,
                ItemName = item.Order?.ItemName ?? "Unknown",
                ExpectedValue = item.Order?.Quantity ?? 0,
                ActualValue = item.Quantity,
                Reason = item.DiscrepancyReason ?? item.State.ToString()
            }),
            AdditionalNotes = receipt.Note ?? "Không có"
        };

        return await _pdfGenerator.GeneratePdfAsync("DiscrepancyReport", data);
    }
}
