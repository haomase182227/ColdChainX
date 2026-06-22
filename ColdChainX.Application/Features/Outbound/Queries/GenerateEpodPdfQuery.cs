using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Queries;

public class GenerateEpodPdfQuery : IRequest<byte[]>
{
    public Guid OrderId { get; set; }

    public GenerateEpodPdfQuery(Guid orderId)
    {
        OrderId = orderId;
    }
}

public class GenerateEpodPdfQueryHandler : IRequestHandler<GenerateEpodPdfQuery, byte[]>
{
    private readonly IApplicationDbContext _context;
    private readonly IPdfGeneratorService _pdfGenerator;

    public GenerateEpodPdfQueryHandler(IApplicationDbContext context, IPdfGeneratorService pdfGenerator)
    {
        _context = context;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<byte[]> Handle(GenerateEpodPdfQuery request, CancellationToken cancellationToken)
    {
        var epod = await _context.DeliveryEpods
            .Include(x => x.Order)
                .ThenInclude(o => o.Customer)
            .Include(x => x.Order)
                .ThenInclude(o => o.DestLocationNavigation)
            .Include(x => x.Order)
                .ThenInclude(o => o.MasterTrip)
                    .ThenInclude(mt => mt.Driver)
            .Include(x => x.Order)
                .ThenInclude(o => o.MasterTrip)
                    .ThenInclude(mt => mt.Vehicle)
            .FirstOrDefaultAsync(x => x.OrderId == request.OrderId, cancellationToken);

        if (epod == null)
            throw new Exception("ePOD not found for this order");

        // The LPNs for this order
        var lpns = await _context.Lpns
            .Include(x => x.Order)
            .Where(x => x.OrderId == request.OrderId)
            .ToListAsync(cancellationToken);

        var data = new
        {
            DeliveryDate = epod.SignedAt?.ToString("dd/MM/yyyy HH:mm") ?? DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            DestinationAddress = epod.Order?.DestLocationNavigation?.Address ?? "N/A",
            CompanyName = "ColdChainX Logistics",
            VehiclePlateNumber = epod.Order?.MasterTrip?.Vehicle?.TruckPlate ?? "N/A",
            DriverName = epod.Order?.MasterTrip?.Driver?.FullName ?? "N/A",
            CustomerName = epod.Order?.Customer?.CompanyName ?? "N/A",
            OrderCode = epod.Order?.TrackingCode ?? "N/A",
            Items = lpns.Select((item, index) => new
            {
                Index = index + 1,
                ItemName = item.Order.ItemName,
                LpnCode = item.LpnCode,
                Unit = "Item",
                Quantity = item.Quantity,
                StatusDescription = item.DiscrepancyReason ?? "Nguyên vẹn"
            })
        };

        return await _pdfGenerator.GeneratePdfAsync("Epod", data);
    }
}
