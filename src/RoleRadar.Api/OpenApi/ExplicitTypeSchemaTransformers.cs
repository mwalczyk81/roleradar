using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RoleRadar.Api.OpenApi;

public static class ExplicitTypeSchemaTransformers
{
    public static OpenApiOptions AddExplicitTypeTransformers(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "RoleRadar.Api | v1";
            document.Info.Version = "1.0.0";
            document.Servers?.Clear();
            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            NormalizeSchema(schema, context.JsonTypeInfo.Type);
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            if (operation.Parameters is not null)
            {
                foreach (var parameter in operation.Parameters)
                {
                    if (parameter.Schema is OpenApiSchema paramSchema)
                    {
                        NormalizeParameterSchema(paramSchema);
                    }
                }
            }

            return Task.CompletedTask;
        });

        return options;
    }

    private static void NormalizeSchema(OpenApiSchema schema, Type clrType)
    {
        var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short))
        {
            var isNullable = Nullable.GetUnderlyingType(clrType) is not null || (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null));
            schema.Type = isNullable ? (JsonSchemaType.Integer | JsonSchemaType.Null) : JsonSchemaType.Integer;
            schema.Pattern = null;
        }
        else if (underlyingType == typeof(decimal) || underlyingType == typeof(double) || underlyingType == typeof(float))
        {
            var isNullable = Nullable.GetUnderlyingType(clrType) is not null || (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null));
            schema.Type = isNullable ? (JsonSchemaType.Number | JsonSchemaType.Null) : JsonSchemaType.Number;
            schema.Pattern = null;
        }
        else if (underlyingType == typeof(bool))
        {
            var isNullable = Nullable.GetUnderlyingType(clrType) is not null || (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null));
            schema.Type = isNullable ? (JsonSchemaType.Boolean | JsonSchemaType.Null) : JsonSchemaType.Boolean;
        }
        else if (underlyingType == typeof(Guid))
        {
            var isNullable = Nullable.GetUnderlyingType(clrType) is not null || (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null));
            schema.Type = isNullable ? (JsonSchemaType.String | JsonSchemaType.Null) : JsonSchemaType.String;
            schema.Format = "uuid";
        }
        else if (underlyingType == typeof(DateTimeOffset) || underlyingType == typeof(DateTime))
        {
            var isNullable = Nullable.GetUnderlyingType(clrType) is not null || (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null));
            schema.Type = isNullable ? (JsonSchemaType.String | JsonSchemaType.Null) : JsonSchemaType.String;
            schema.Format = "date-time";
        }
        else if (underlyingType == typeof(DateOnly))
        {
            var isNullable = Nullable.GetUnderlyingType(clrType) is not null || (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null));
            schema.Type = isNullable ? (JsonSchemaType.String | JsonSchemaType.Null) : JsonSchemaType.String;
            schema.Format = "date";
        }

        if (schema.Properties is not null)
        {
            foreach (var (_, propSchema) in schema.Properties)
            {
                if (propSchema is OpenApiSchema concreteProp)
                {
                    NormalizeParameterSchema(concreteProp);
                }
            }
        }
    }

    private static void NormalizeParameterSchema(OpenApiSchema schema)
    {
        if (!schema.Type.HasValue)
        {
            return;
        }

        var type = schema.Type.Value;
        var hasNull = type.HasFlag(JsonSchemaType.Null);

        if (type.HasFlag(JsonSchemaType.Integer) && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = hasNull ? (JsonSchemaType.Integer | JsonSchemaType.Null) : JsonSchemaType.Integer;
            schema.Pattern = null;
        }
        else if (type.HasFlag(JsonSchemaType.Number) && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = hasNull ? (JsonSchemaType.Number | JsonSchemaType.Null) : JsonSchemaType.Number;
            schema.Pattern = null;
        }
    }
}
