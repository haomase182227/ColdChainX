using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inventory.Commands;

public class UpdateLpnCommand : IRequest<UpdateLpnResponse>
{
    public Guid LpnId { get; set; }
    public string? BatchNumber { get; set; }
    public string? StorageLocation { get; set; }
    public string? DiscrepancyReason { get; set; }
}

public class UpdateLpnResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UpdateLpnCommandHandler : IRequestHandler<UpdateLpnCommand, UpdateLpnResponse>
{
    private readonly IApplicationDbContext _context;

    public UpdateLpnCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UpdateLpnResponse> Handle(UpdateLpnCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(x => x.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return new UpdateLpnResponse { Success = false, Message = "LPN không tồn tại trong hệ thống." };

        lpn.BatchNumber = request.BatchNumber ?? lpn.BatchNumber;
        lpn.StorageLocation = request.StorageLocation ?? lpn.StorageLocation;
        lpn.DiscrepancyReason = request.DiscrepancyReason ?? lpn.DiscrepancyReason;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateLpnResponse { Success = true, Message = "Cập nhật LPN thành công." };
    }
}
