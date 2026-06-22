using MediatR;

namespace ColdChainX.Application.Features.Outbound.Commands;

public class PickLpnCommand : IRequest<PickLpnResponse>
{
    public Guid LpnId { get; set; }
}

public class PickLpnResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
