using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class CreateCustomerOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            var httpMethod = context.ApiDescription.HttpMethod;

            if (!string.Equals(path, "api/auth/create-customer", StringComparison.OrdinalIgnoreCase)
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
                                ["phone"] = new OpenApiSchema { Type = "string", Description = "Phone number (optional)" },
                                ["companyName"] = new OpenApiSchema { Type = "string", Description = "Company name (required)" },
                                ["taxCode"] = new OpenApiSchema { Type = "string", Description = "Tax code (required)" },
                                ["address"] = new OpenApiSchema { Type = "string", Description = "Address (optional)" },
                                ["paymentTerm"] = new OpenApiSchema { Type = "integer", Format = "int32", Description = "Payment term in days (optional, defaults to 30)" }
                            },
                            Required = new HashSet<string> { "fullName", "email", "password", "companyName", "taxCode" }
                        }
                    }
                }
            };
        }
    }
}
