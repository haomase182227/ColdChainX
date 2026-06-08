using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.Validators;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class CreateOrderRequestSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type != typeof(CreateOrderRequest))
                return;

            ApplyEnum(schema, "category", CreateOrderRequestValidator.AllowedCategories);
            ApplyEnum(schema, "Category", CreateOrderRequestValidator.AllowedCategories);
            ApplyEnum(schema, "packagingType", CreateOrderRequestValidator.AllowedPackagingTypes);
            ApplyEnum(schema, "PackagingType", CreateOrderRequestValidator.AllowedPackagingTypes);
            ApplyTemperatureRange(schema, "tempCondition");
            ApplyTemperatureRange(schema, "TempCondition");
        }

        private static void ApplyEnum(OpenApiSchema schema, string propertyName, IEnumerable<string> values)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null)
                return;

            property.Type = "string";
            property.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }

        private static void ApplyTemperatureRange(OpenApiSchema schema, string propertyName)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null)
                return;

            property.Type = "number";
            property.Format = "double";
            property.Minimum = -18;
            property.Maximum = -5;
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
