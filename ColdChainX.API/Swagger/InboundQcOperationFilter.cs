using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class InboundQcOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            var httpMethod = context.ApiDescription.HttpMethod;

            if (!string.Equals(path, "api/Inbound/qc", StringComparison.OrdinalIgnoreCase)
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

            ApplyStringExample(
                mediaType.Schema,
                "Actual_Package_Lines",
                """[{"label":"Thung 5kg","quantity":4,"actualWeightKg":22,"lengthCm":35,"widthCm":25,"heightCm":20},{"label":"Thung 10kg","quantity":6,"actualWeightKg":63,"lengthCm":45,"widthCm":30,"heightCm":25}]""",
                "Paste JSON array of actual package lines measured by warehouse QC.");

            RemoveProperties(mediaType.Schema,
                "WarehouseId",
                "ActualWeightKg",
                "LengthCm",
                "WidthCm",
                "HeightCm");
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

        private static OpenApiSchema? FindProperty(OpenApiSchema schema, string propertyName)
        {
            if (schema.Properties.TryGetValue(propertyName, out var exactMatch))
                return exactMatch;

            return schema.Properties
                .FirstOrDefault(entry => string.Equals(entry.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                .Value;
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
