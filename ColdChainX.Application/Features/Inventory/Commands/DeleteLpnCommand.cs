using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inventory.Commands;

public class DeleteLpnCommand : IRequest<DeleteLpnResponse>
{
    public Guid LpnId { get; set; }

    public DeleteLpnCommand(Guid lpnId)
    {
        LpnId = lpnId;
    }
}

public class DeleteLpnResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DeleteLpnCommandHandler : IRequestHandler<DeleteLpnCommand, DeleteLpnResponse>
{
    private readonly IApplicationDbContext _context;

    public DeleteLpnCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DeleteLpnResponse> Handle(DeleteLpnCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(x => x.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return new DeleteLpnResponse { Success = false, Message = "LPN không tồn tại trong hệ thống." };

        _context.Lpns.Remove(lpn);
        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteLpnResponse { Success = true, Message = "Xóa LPN thành công." };
    }
}
