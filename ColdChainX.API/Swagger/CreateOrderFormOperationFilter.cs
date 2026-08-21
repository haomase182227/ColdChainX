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
            ApplyStringExample(
                mediaType.Schema,
                "Package_Lines",
                """[{"capacityKg":5,"quantity":4,"sizeClass":"M"},{"capacityKg":10,"quantity":6,"sizeClass":"M"},{"capacityKg":22,"quantity":3,"sizeClass":"L"}]""",
                "Paste JSON array of package lines. sizeClass supports S, M, L, XL. Backend generates label from capacityKg and calculates ExpectedWeightKg and ExpectedCbm from this value.");

            RemoveProperties(mediaType.Schema,
                "Length_CM",
                "Width_CM",
                "Height_CM",
                "Expected_Weight_KG",
                "Quantity",
                "Customer_Provided_Total_CBM");
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

        private static void ApplyStringExample(OpenApiSchema schema, string propertyName, string example, string description)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null)
                return;

            property.Type = "string";
            property.Example = new OpenApiString(example);
            property.Default = new OpenApiString(example);
            property.Description = description;
        }

        private static void RemoveProperties(OpenApiSchema schema, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var key = schema.Properties.Keys
                    .FirstOrDefault(existing => string.Equals(existing, propertyName, StringComparison.OrdinalIgnoreCase));

                if (key != null)
                    schema.Properties.Remove(key);

                schema.Required?.Remove(propertyName);
                var requiredKey = schema.Required?
                    .FirstOrDefault(existing => string.Equals(existing, propertyName, StringComparison.OrdinalIgnoreCase));
                if (requiredKey != null)
                    schema.Required.Remove(requiredKey);
            }
        }
    }
}
