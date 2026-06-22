using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class GenerateWarehouseReceiptCommand : IRequest<GenerateWarehouseReceiptResponse>
{
    public Guid AsnId { get; set; }
    public string DelivererName { get; set; } = string.Empty;
    public string? VehiclePlate { get; set; }
    public string? Note { get; set; }
}

public class GenerateWarehouseReceiptRequest
{
    public Guid AsnId { get; set; }
    public string DelivererName { get; set; } = string.Empty;
    public string? VehiclePlate { get; set; }
    public string? Note { get; set; }
}

public class GenerateWarehouseReceiptResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? ReceiptId { get; set; }
    public string? PdfUrl { get; set; }
}

public class GenerateWarehouseReceiptCommandHandler : IRequestHandler<GenerateWarehouseReceiptCommand, GenerateWarehouseReceiptResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<GenerateWarehouseReceiptCommandHandler> _logger;
    private readonly IMediator _mediator;
    private readonly IFileService _fileService;

    public GenerateWarehouseReceiptCommandHandler(
        IApplicationDbContext context, 
        ILogger<GenerateWarehouseReceiptCommandHandler> logger,
        IMediator mediator,
        IFileService fileService)
    {
        _context = context;
        _logger = logger;
        _mediator = mediator;
        _fileService = fileService;
    }

    public async Task<GenerateWarehouseReceiptResponse> Handle(GenerateWarehouseReceiptCommand request, CancellationToken cancellationToken)
    {
        if (request.AsnId == Guid.Empty)
            return new GenerateWarehouseReceiptResponse { Success = false, Message = "AsnId is required." };
            
        if (string.IsNullOrWhiteSpace(request.DelivererName))
            return new GenerateWarehouseReceiptResponse { Success = false, Message = "DelivererName is required." };

        var asn = await _context.InboundAsns
            .Include(a => a.Order)
            .FirstOrDefaultAsync(a => a.AsnId == request.AsnId, cancellationToken);

        if (asn?.Order == null)
            return new GenerateWarehouseReceiptResponse { Success = false, Message = "ASN or linked order was not found." };

        if (!asn.WarehouseId.HasValue)
            return new GenerateWarehouseReceiptResponse { Success = false, Message = "ASN does not have a linked warehouse." };

        var order = asn.Order;
        
        var receipt = await _context.WarehouseReceipts
            .FirstOrDefaultAsync(r => r.OrderId == order.OrderId && r.WarehouseId == asn.WarehouseId.Value && r.PdfUrl == null, cancellationToken);

        if (receipt == null)
            return new GenerateWarehouseReceiptResponse { Success = false, Message = "No pending warehouse receipt found for this ASN." };

        receipt.DelivererName = request.DelivererName;
        receipt.Note = request.Note ?? "Manually updated deliverer info.";
        
        await _context.SaveChangesAsync(cancellationToken);

        var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GenerateReceiptPdfQuery(receipt.ReceiptId), cancellationToken);
            
        var pdfFileName = $"grn-{order.TrackingCode}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var pdfUrl = await _fileService.UploadFileAsync(pdfBytes, pdfFileName);

        receipt.PdfUrl = pdfUrl;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Manually generated warehouse receipt {ReceiptId} for ASN {AsnId}", receipt.ReceiptId, request.AsnId);

        return new GenerateWarehouseReceiptResponse
        {
            Success = true,
            Message = "Warehouse receipt generated successfully.",
            ReceiptId = receipt.ReceiptId,
            PdfUrl = pdfUrl
        };
    }
}
