using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
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
    // Trang thai an toan duoc phep danh dau xoa
    private static readonly LpnState[] DeletableStates =
    {
        LpnState.EXPECTED,
        LpnState.IN_STOCK,
        LpnState.DISCREPANCY_HOLD,
        LpnState.RETURN_PENDING
    };

    private readonly IApplicationDbContext _context;

    public DeleteLpnCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Danh dau LPN la DELETED (soft-delete) — khong xoa du lieu khoi database.
    ///
    /// Precondition : LPN.State phai la EXPECTED / IN_STOCK / DISCREPANCY_HOLD / RETURN_PENDING
    /// Postcondition: LPN.State = DELETED (van con trong DB, khong the dung trong bat ky quy trinh nao)
    ///
    /// Tu choi neu LPN dang trong quy trinh xuat kho
    /// (ALLOCATED / LOADING / LOADING_COMPLETED / RELEASED / SHIPPING).
    /// </summary>
    public async Task<DeleteLpnResponse> Handle(DeleteLpnCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(x => x.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return new DeleteLpnResponse { Success = false, Message = "LPN không tồn tại trong hệ thống." };

        if (lpn.State == LpnState.DELETED)
            return new DeleteLpnResponse { Success = false, Message = $"LPN '{lpn.LpnCode}' đã bị xóa trước đó." };

        if (!DeletableStates.Contains(lpn.State))
            return new DeleteLpnResponse
            {
                Success = false,
                Message = $"Không thể xóa LPN '{lpn.LpnCode}' — đang ở trạng thái {lpn.State}. " +
                          $"Chỉ được xóa LPN ở trạng thái: {string.Join(", ", DeletableStates)}."
            };

        lpn.State = LpnState.DELETED;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteLpnResponse { Success = true, Message = $"LPN '{lpn.LpnCode}' đã được đánh dấu DELETED." };
    }
}
