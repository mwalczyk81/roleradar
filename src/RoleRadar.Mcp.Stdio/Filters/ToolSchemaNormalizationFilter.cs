using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoleRadar.Mcp.Stdio.Filters;

/// <summary>
/// Normalizes tool JSON schemas for all 12 MCP tools so that every parameter
/// explicitly declares a scalar string type (e.g. "type": "string" with "nullable": true)
/// rather than array type schemas (e.g. ["string", "null"]) which are treated as untyped
/// by OpenAPI 3.0 / Claude tool callers.
/// </summary>
public static class ToolSchemaNormalizationFilter
{
    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Create()
    {
        return next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            if (result?.Tools != null)
            {
                foreach (var tool in result.Tools)
                {
                    tool.InputSchema = NormalizeInputSchema(tool.InputSchema);
                }
            }

            return result!;
        };
    }

    public static JsonElement NormalizeInputSchema(JsonElement inputSchema)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            return inputSchema;
        }

        var node = JsonNode.Parse(inputSchema.GetRawText());
        if (node is not JsonObject rootObj)
        {
            return inputSchema;
        }

        if (rootObj["properties"] is JsonObject propertiesObj)
        {
            var requiredSet = new HashSet<string>(StringComparer.Ordinal);
            if (rootObj["required"] is JsonArray requiredArr)
            {
                foreach (var item in requiredArr)
                {
                    if (item != null)
                    {
                        requiredSet.Add(item.ToString());
                    }
                }
            }

            foreach (var prop in propertiesObj)
            {
                if (prop.Value is JsonObject propObj)
                {
                    NormalizePropertySchema(prop.Key, propObj, requiredSet.Contains(prop.Key));
                }
            }
        }

        return JsonSerializer.SerializeToElement(rootObj);
    }

    private static void NormalizePropertySchema(string propName, JsonObject propObj, bool isRequired)
    {
        // 1. If type is an array, e.g. ["string", "null"] or ["number", "null"]
        if (propObj["type"] is JsonArray typeArr)
        {
            string? baseType = null;
            var isNullable = false;

            foreach (var item in typeArr)
            {
                var s = item?.ToString();
                if (s == "null")
                {
                    isNullable = true;
                }
                else if (!string.IsNullOrEmpty(s))
                {
                    baseType = s;
                }
            }

            if (!string.IsNullOrEmpty(baseType))
            {
                propObj["type"] = baseType;
                if (isNullable || !isRequired)
                {
                    propObj["nullable"] = true;
                }
            }
        }
        else if (propObj["type"] == null)
        {
            // If type is missing, declare "type": "string" (nullable)
            propObj["type"] = "string";
            if (!isRequired)
            {
                propObj["nullable"] = true;
            }
        }
        else
        {
            // type is already a single scalar string
            if (!isRequired)
            {
                propObj["nullable"] = true;
            }
        }

        // If items schema is present (array type), normalize items if needed
        if (propObj["items"] is JsonObject itemsObj)
        {
            if (itemsObj["type"] is JsonArray itemTypeArr)
            {
                var baseItemType = itemTypeArr.FirstOrDefault(i => i?.ToString() != "null")?.ToString();
                if (!string.IsNullOrEmpty(baseItemType))
                {
                    itemsObj["type"] = baseItemType;
                }
            }
        }
    }
}
