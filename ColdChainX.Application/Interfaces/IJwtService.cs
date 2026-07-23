using System;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces
{
    public interface IJwtService
    {
        string GenerateAccessToken(User user, DateTime expiresAt, Guid? customerId = null, Guid? driverId = null);
        string GenerateRefreshToken();
    }
}
