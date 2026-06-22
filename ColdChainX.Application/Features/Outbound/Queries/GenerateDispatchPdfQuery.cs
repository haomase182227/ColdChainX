using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Queries;

public class GenerateDispatchPdfQuery : IRequest<byte[]>
{
    public Guid MasterTripId { get; set; }

    public GenerateDispatchPdfQuery(Guid masterTripId)
    {
        MasterTripId = masterTripId;
    }
}

public class GenerateDispatchPdfQueryHandler : IRequestHandler<GenerateDispatchPdfQuery, byte[]>
{
    private readonly IApplicationDbContext _context;
    private readonly IPdfGeneratorService _pdfGenerator;

    public GenerateDispatchPdfQueryHandler(IApplicationDbContext context, IPdfGeneratorService pdfGenerator)
    {
        _context = context;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<byte[]> Handle(GenerateDispatchPdfQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips
            .Include(x => x.Driver)
            .Include(x => x.Vehicle)
            .Include(x => x.OriginLocation)
            .Include(x => x.DestinationLocation)
            .FirstOrDefaultAsync(x => x.TripId == request.MasterTripId, cancellationToken);

        if (trip == null)
            throw new Exception("Master Trip not found");

        var lpns = await _context.Lpns
            .Include(x => x.Order)
            .Where(x => x.TripId == request.MasterTripId)
            .ToListAsync(cancellationToken);

        var data = new
        {
            CompanyName = "ColdChainX Logistics",
            DispatchCode = trip.TripId.ToString().Substring(0, 8).ToUpper(),
            DispatchDate = trip.PlannedStartTime.ToString("dd/MM/yyyy"),
            HubName = trip.OriginLocation?.Address ?? "N/A",
            HubAddress = trip.OriginLocation?.Address ?? "N/A",
            DriverName = trip.Driver?.FullName ?? "N/A",
            VehiclePlateNumber = trip.Vehicle?.TruckPlate ?? "N/A",
            SealNumber = trip.SealNumber ?? "N/A",
            CreatedDay = DateTime.Now.Day.ToString("00"),
            CreatedMonth = DateTime.Now.Month.ToString("00"),
            CreatedYear = DateTime.Now.Year.ToString(),
            DestinationDescription = trip.DestinationLocation?.Address ?? "N/A",
            Items = lpns.Select((item, index) => new
            {
                Index = index + 1,
                ItemName = item.Order.ItemName,
                LpnCode = item.LpnCode,
                Unit = "Item",
                ExportedQty = item.Quantity
            })
        };

        return await _pdfGenerator.GeneratePdfAsync("InternalExport", data);
    }
}
