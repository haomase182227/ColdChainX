using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    /// <summary>
    /// Removes authorization requirement from public register endpoint
    /// </summary>
    public class RemoveAuthFromCreateAccountsFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');

            // Remove auth requirement only for public register endpoint
            if (string.Equals(path, "api/auth/register", StringComparison.OrdinalIgnoreCase))
            {
                operation.Security?.Clear();
            }
        }
    }
}
