using System;

namespace ColdChainX.Shared.Exceptions
{
    public class ConflictException : ApiException
    {
        public ConflictException(string message) : base(message, 409)
        {
        }
    }
}
