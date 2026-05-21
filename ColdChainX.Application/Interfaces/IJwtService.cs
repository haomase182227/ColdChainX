using System;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces
{
    public interface IJwtService
    {
        string GenerateAccessToken(User user, DateTime expiresAt);
        string GenerateRefreshToken();
    }
}
