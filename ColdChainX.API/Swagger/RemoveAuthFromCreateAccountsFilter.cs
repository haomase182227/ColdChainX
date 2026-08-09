using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class RemoveAuthFromCreateAccountsFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            var allowsAnonymous =
                context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any() ||
                context.MethodInfo.DeclaringType?.GetCustomAttributes(true)
                    .OfType<AllowAnonymousAttribute>()
                    .Any() == true;

            if (allowsAnonymous ||
                string.Equals(path, "api/auth/register", StringComparison.OrdinalIgnoreCase))
            {
                operation.Security?.Clear();
            }
        }
    }
}
