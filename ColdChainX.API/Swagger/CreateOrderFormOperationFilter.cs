using ColdChainX.Application.Validators;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class CreateOrderFormOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            var httpMethod = context.ApiDescription.HttpMethod;

            if (!string.Equals(path, "api/orders", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (operation.RequestBody?.Content == null
                || !operation.RequestBody.Content.TryGetValue("multipart/form-data", out var mediaType)
                || mediaType.Schema == null)
            {
                return;
            }

            ApplyEnum(mediaType.Schema, "Category", CreateOrderRequestValidator.AllowedCategories);
            ApplyEnum(mediaType.Schema, "category", CreateOrderRequestValidator.AllowedCategories);
            ApplyEnum(mediaType.Schema, "Packaging_Type", CreateOrderRequestValidator.AllowedPackagingTypes);
            ApplyEnum(mediaType.Schema, "packagingType", CreateOrderRequestValidator.AllowedPackagingTypes);
        }

        private static void ApplyEnum(OpenApiSchema schema, string propertyName, IEnumerable<string> values)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null)
                return;

            property.Type = "string";
            property.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }

        private static OpenApiSchema? FindProperty(OpenApiSchema schema, string propertyName)
        {
            if (schema.Properties.TryGetValue(propertyName, out var exactMatch))
                return exactMatch;

            return schema.Properties
                .FirstOrDefault(entry => string.Equals(entry.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                .Value;
        }
    }
}
