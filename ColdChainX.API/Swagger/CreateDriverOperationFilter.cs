using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class CreateDriverOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            var httpMethod = context.ApiDescription.HttpMethod;

            if (!string.Equals(path, "api/auth/create-driver", StringComparison.OrdinalIgnoreCase)
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
                                ["dateOfBirth"] = new OpenApiSchema { Type = "string", Format = "date", Description = "Date of birth (required, format: YYYY-MM-DD)" },
                                ["licenseNumber"] = new OpenApiSchema { Type = "string", Description = "Driver license number (optional)" },
                                ["licenseClass"] = new OpenApiSchema { Type = "string", Description = "License class (optional, e.g., B, C, CE)" },
                                ["issueDate"] = new OpenApiSchema { Type = "string", Format = "date", Description = "License issue date (optional, format: YYYY-MM-DD)" },
                                ["expiryDate"] = new OpenApiSchema { Type = "string", Format = "date", Description = "License expiry date (optional, format: YYYY-MM-DD)" },
                                ["documentUrl"] = new OpenApiSchema { Type = "string", Description = "License document URL (optional)" }
                            },
                            Required = new HashSet<string> { "fullName", "email", "password", "dateOfBirth" }
                        }
                    }
                }
            };
        }
    }
}
