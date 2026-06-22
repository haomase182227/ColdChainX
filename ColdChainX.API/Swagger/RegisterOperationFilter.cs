using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class RegisterOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            var httpMethod = context.ApiDescription.HttpMethod;

            if (!string.Equals(path, "api/auth/register", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Ensure multipart/form-data is properly displayed
            operation.RequestBody = new OpenApiRequestBody
            {
                Content =
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties =
                            {
                                ["username"] = new OpenApiSchema { Type = "string", Description = "Username (optional, defaults to email)" },
                                ["fullName"] = new OpenApiSchema { Type = "string", Description = "Full name (required)" },
                                ["email"] = new OpenApiSchema { Type = "string", Format = "email", Description = "Email address (required)" },
                                ["password"] = new OpenApiSchema { Type = "string", Format = "password", Description = "Password (required, min 6 characters)" },
                                ["role"] = new OpenApiSchema 
                                { 
                                    Type = "string", 
                                    Description = "Role (required, allowed values: Admin, Dispatcher, Sales, Loader)", 
                                    Default = new OpenApiString("Dispatcher"),
                                    Enum = new List<IOpenApiAny>
                                    {
                                        new OpenApiString("Admin"),
                                        new OpenApiString("Dispatcher"),
                                        new OpenApiString("Sales"),
                                        new OpenApiString("Loader")
                                    }
                                }
                            },
                            Required = new HashSet<string> { "fullName", "email", "password", "role" }
                        }
                    }
                }
            };
        }
    }
}
