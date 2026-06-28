using System;

namespace ColdChainX.Shared.Exceptions
{
    public class ExternalServiceException : ApiException
    {
        public ExternalServiceException(string message) : base(message, 503)
        {
        }
    }
}
