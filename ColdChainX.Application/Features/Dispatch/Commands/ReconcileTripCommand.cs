using ColdChainX.Shared.Responses;
using MediatR;
using System;

namespace ColdChainX.Application.Features.Dispatch.Commands
{
    public class ReconcileTripCommand : IRequest<ApiResponse<bool>>
    {
        public Guid TripId { get; set; }
        public decimal RemittedAmount { get; set; }
        public string? Note { get; set; }
        public Guid UserId { get; set; }
    }
}
