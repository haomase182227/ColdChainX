using MediatR;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ProcessInboundQcCommand : IRequest<ProcessInboundQcResponse>
{
    public Guid LpnId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ReceiverId { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
}

public class ProcessInboundQcResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PdfUrl { get; set; }
}
