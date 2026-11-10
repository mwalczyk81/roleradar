using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RoleRadar.Api.OpenApi;

/// <summary>
/// Gives the OpenAPI schemas of the platform's problem documents the two members this API always adds, <c>errorCode</c> and
/// <c>traceId</c> (feature 014, research R-007, ADR 0044). They are extension members at run time, so the platform's schema does
/// not list them; declaring them as strings puts them in the generated TypeScript client and the web reads them without a cast.
/// Runs while the document is generated, including at build time with no Mongo, and writes nothing to the log.
/// </summary>
public static class ProblemDetailsSchemaTransformer
{
    public static OpenApiOptions AddProblemDetailsMembers(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            var type = context.JsonTypeInfo.Type;
            if (type == typeof(Microsoft.AspNetCore.Mvc.ProblemDetails) || type == typeof(HttpValidationProblemDetails))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
                schema.Properties["errorCode"] = new OpenApiSchema { Type = JsonSchemaType.String };
                schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
