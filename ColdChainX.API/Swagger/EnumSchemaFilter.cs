using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Linq;

namespace ColdChainX.API.Swagger
{
    public class EnumSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type is null || !context.Type.IsEnum)
                return;

            var enumNames = Enum.GetNames(context.Type);

            schema.Type = "string";
            schema.Format = null;
            schema.Enum = enumNames
                .Select(name => (IOpenApiAny)new OpenApiString(name))
                .ToList();
        }
    }
}
