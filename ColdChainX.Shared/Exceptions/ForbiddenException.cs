using System;

namespace ColdChainX.Shared.Exceptions
{
    public class ForbiddenException : ApiException
    {
        public ForbiddenException(string message) : base(message, 403)
        {
        }
    }
}
