using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CryptoBlade.Helpers
{
    public sealed class StringEnumSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            var t = context.Type;
            if (t.IsEnum)
            {
                schema.Type = "string";
                schema.Format = null;
                schema.Enum = Enum.GetNames(t)
                    .Select(n => (IOpenApiAny)new OpenApiString(n))
                    .ToList();
            }
        }
    }

}
