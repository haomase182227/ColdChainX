using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public sealed class CommonApiResponsesOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            AddIfMissing(operation, StatusCodes.Status400BadRequest, "Bad request.");
            AddIfMissing(operation, StatusCodes.Status404NotFound, "Resource not found.");
            AddIfMissing(operation, StatusCodes.Status409Conflict, "Business rule conflict.");
            AddIfMissing(operation, StatusCodes.Status500InternalServerError, "Unexpected server error.");

            if (RequiresAuthentication(context))
            {
                AddIfMissing(operation, StatusCodes.Status401Unauthorized, "Authentication is required or the access token is invalid.");
                AddIfMissing(operation, StatusCodes.Status403Forbidden, "The authenticated user does not have permission for this operation.");
            }
        }

        private static bool RequiresAuthentication(OperationFilterContext context)
        {
            var allowAnonymous =
                context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any() ||
                context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any() == true;

            if (allowAnonymous)
                return false;

            return
                context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any() ||
                context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any() == true;
        }

        private static void AddIfMissing(OpenApiOperation operation, int statusCode, string description)
        {
            operation.Responses.TryAdd(statusCode.ToString(), new OpenApiResponse
            {
                Description = description
            });
        }
    }
}
