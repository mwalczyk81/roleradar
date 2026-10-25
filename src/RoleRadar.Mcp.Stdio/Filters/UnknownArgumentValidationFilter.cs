using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Mcp.Stdio.Tools;

namespace RoleRadar.Mcp.Stdio.Filters;

/// <summary>
/// Intercepts tool calls and rejects unknown or misshapen arguments with a ValidationError envelope,
/// coerces signal shorthand to object form, and protects against generic SDK binding failures.
/// </summary>
public static class UnknownArgumentValidationFilter
{
    private static readonly Dictionary<string, Dictionary<string, ParameterInfo>> KnownToolParameters = BuildKnownToolParameters();

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create()
    {
        return next => async (context, cancellationToken) =>
        {
            return await ExecuteAsync(
                context.Params,
                async p => await next(context, cancellationToken),
                cancellationToken);
        };
    }

    public static async ValueTask<CallToolResult> ExecuteAsync(
        CallToolRequestParams requestParams,
        Func<CallToolRequestParams, ValueTask<CallToolResult>> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestParams);
        ArgumentNullException.ThrowIfNull(next);

        var toolName = requestParams.Name;
        var args = requestParams.Arguments;

        // 1. If tool is unknown, allow next to handle or fail
        if (!KnownToolParameters.TryGetValue(toolName, out var validParams))
        {
            return await next(requestParams);
        }

        // 2. Reject unknown arguments
        if (args != null && args.Count > 0)
        {
            var unknownArgs = args.Keys
                .Where(k => !validParams.ContainsKey(k))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            if (unknownArgs.Count > 0)
            {
                var errorMessage = $"Unknown argument(s): {string.Join(", ", unknownArgs.Select(a => $"'{a}'"))}.";
                return CreateValidationErrorResult(errorMessage);
            }
        }

        // 3. Pre-validate parameter shapes & coerce signals shorthand
        if (args != null && args.Count > 0)
        {
            foreach (var (paramName, paramInfo) in validParams)
            {
                if (args.TryGetValue(paramName, out var element))
                {
                    var shapeError = ValidateArgumentShape(paramName, paramInfo, element);
                    if (shapeError != null)
                    {
                        return CreateValidationErrorResult(shapeError);
                    }
                }
            }

            // If tool is posting_record and has signals, coerce string shorthand to object form in-place
            if (string.Equals(toolName, "posting_record", StringComparison.OrdinalIgnoreCase)
                && args.TryGetValue("signals", out var signalsElement)
                && signalsElement.ValueKind == JsonValueKind.Array
                && !args.IsReadOnly)
            {
                CoerceSignalsArgument(args, signalsElement);
            }
        }

        // 4. Invoke tool pipeline with exception & generic SDK error interception
        try
        {
            var result = await next(requestParams);

            if (result.IsError == true)
            {
                var contentText = GetFirstContentText(result);
                if (contentText != null && contentText.Trim().StartsWith('{') && contentText.Contains("\"ok\"", StringComparison.Ordinal))
                {
                    // Already a structured RoleRadar JSON envelope; normalize IsError to false for client consistency
                    result.IsError = false;
                    return result;
                }

                Console.Error.WriteLine($"[ERROR] Unexpected error invoking tool '{toolName}': {contentText}");
                var msg = ExtractErrorMessage(result, toolName);
                return CreateValidationErrorResult(msg);
            }

            return result;
        }
        catch (Exception ex)
        {
            var actualEx = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Console.Error.WriteLine($"[ERROR] Unexpected exception invoking tool '{toolName}': {actualEx.GetType().FullName}: {actualEx.Message}\n{actualEx.StackTrace}");
            var msg = ExtractExceptionMessage(actualEx, toolName, validParams);
            return CreateValidationErrorResult(msg);
        }
    }

    private static string? ValidateArgumentShape(string paramName, ParameterInfo paramInfo, JsonElement element)
    {
        var type = paramInfo.ParameterType;
        var nonNullType = Nullable.GetUnderlyingType(type) ?? type;
        var isNullable = Nullable.GetUnderlyingType(type) != null || !type.IsValueType || paramInfo.HasDefaultValue;

        if (element.ValueKind == JsonValueKind.Null)
        {
            if (isNullable)
            {
                return null;
            }

            return $"Missing required argument '{paramName}': expected {GetExpectedShape(paramInfo)}.";
        }

        if (nonNullType == typeof(Guid))
        {
            if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out _))
            {
                return $"Invalid argument for '{paramName}': expected UUID string.";
            }

            return null;
        }

        if (nonNullType == typeof(string))
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                return $"Invalid argument for '{paramName}': expected string.";
            }

            return null;
        }

        if (nonNullType == typeof(string[]) || nonNullType == typeof(IReadOnlyList<string>))
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return $"Invalid argument for '{paramName}': expected array of strings.";
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return $"Invalid argument for '{paramName}': expected array of strings.";
                }
            }

            return null;
        }

        if (nonNullType == typeof(int) || nonNullType == typeof(decimal))
        {
            if (element.ValueKind != JsonValueKind.Number)
            {
                return $"Invalid argument for '{paramName}': expected number.";
            }

            return null;
        }

        if (nonNullType == typeof(bool))
        {
            if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
            {
                return $"Invalid argument for '{paramName}': expected boolean.";
            }

            return null;
        }

        if (nonNullType == typeof(SignalInputDto[]) || nonNullType == typeof(IReadOnlyList<SignalInputDto>))
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return $"Invalid argument for '{paramName}': expected array of signal strings or objects.";
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String && item.ValueKind != JsonValueKind.Object)
                {
                    return $"Invalid argument for '{paramName}': expected array of signal strings or objects.";
                }
            }

            return null;
        }

        if (nonNullType == typeof(CareerSourceInputDto[]) || nonNullType == typeof(IReadOnlyList<CareerSourceInputDto>))
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return $"Invalid argument for '{paramName}': expected array of career source objects.";
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return $"Invalid argument for '{paramName}': expected array of career source objects.";
                }
            }

            return null;
        }

        if (nonNullType == typeof(ApplicationEventInputDto))
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return $"Invalid argument for '{paramName}': expected timeline event object.";
            }

            return null;
        }

        return null;
    }

    private static void CoerceSignalsArgument(IDictionary<string, JsonElement> args, JsonElement signalsElement)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var item in signalsElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var str = item.GetString()?.Trim() ?? string.Empty;
                if (str.StartsWith("deadline:", StringComparison.OrdinalIgnoreCase))
                {
                    var deadlineDate = str["deadline:".Length..].Trim();
                    list.Add(new Dictionary<string, object?>
                    {
                        ["signalType"] = "deadline",
                        ["evidence"] = "reported by agent",
                        ["deadlineDate"] = deadlineDate
                    });
                }
                else
                {
                    list.Add(new Dictionary<string, object?>
                    {
                        ["signalType"] = str,
                        ["evidence"] = "reported by agent"
                    });
                }
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var obj = new Dictionary<string, object?>();
                foreach (var prop in item.EnumerateObject())
                {
                    obj[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }

                if (!obj.TryGetValue("evidence", out var evVal) || string.IsNullOrWhiteSpace(evVal?.ToString()))
                {
                    obj["evidence"] = "reported by agent";
                }

                list.Add(obj);
            }
        }

        var json = JsonSerializer.Serialize(list);
        using var doc = JsonDocument.Parse(json);
        args["signals"] = doc.RootElement.Clone();
    }

    private static string GetExpectedShape(ParameterInfo pi)
    {
        var type = pi.ParameterType;
        var nonNullType = Nullable.GetUnderlyingType(type) ?? type;

        if (nonNullType == typeof(Guid))
        {
            return "UUID string";
        }

        if (nonNullType == typeof(string))
        {
            return "string";
        }

        if (nonNullType == typeof(string[]) || nonNullType == typeof(IReadOnlyList<string>))
        {
            return "array of strings";
        }

        if (nonNullType == typeof(int) || nonNullType == typeof(decimal))
        {
            return "number";
        }

        if (nonNullType == typeof(bool))
        {
            return "boolean";
        }

        if (nonNullType == typeof(SignalInputDto[]) || nonNullType == typeof(IReadOnlyList<SignalInputDto>))
        {
            return "array of signal strings or objects";
        }

        if (nonNullType == typeof(CareerSourceInputDto[]) || nonNullType == typeof(IReadOnlyList<CareerSourceInputDto>))
        {
            return "array of career source objects";
        }

        if (nonNullType == typeof(ApplicationEventInputDto))
        {
            return "timeline event object";
        }

        return nonNullType.Name;
    }

    private static string ExtractErrorMessage(CallToolResult result, string toolName)
    {
        var text = GetFirstContentText(result);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim();
            if (trimmed.Contains("error occurred invoking", StringComparison.OrdinalIgnoreCase))
            {
                return $"Invalid argument for '{toolName}': invocation failure.";
            }
        }

        return $"Invalid argument for '{toolName}'.";
    }

    private static string ExtractExceptionMessage(Exception ex, string toolName, Dictionary<string, ParameterInfo> validParams)
    {
        var actualEx = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;

        if (actualEx is JsonException jex)
        {
            if (!string.IsNullOrWhiteSpace(jex.Path))
            {
                var segments = jex.Path.TrimStart('$', '.').Split('.', '[', ']');
                var firstSegment = segments.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                if (firstSegment != null && validParams.TryGetValue(firstSegment, out var pi))
                {
                    return $"Invalid argument for '{firstSegment}': expected {GetExpectedShape(pi)}.";
                }
            }

            return $"Invalid argument for '{toolName}': deserialization failure.";
        }

        if (actualEx is ArgumentException aex && !string.IsNullOrWhiteSpace(aex.ParamName) && validParams.TryGetValue(aex.ParamName, out var paramInfo))
        {
            return $"Invalid argument for '{aex.ParamName}': expected {GetExpectedShape(paramInfo)}.";
        }

        return $"Invalid argument for '{toolName}'.";
    }

    private static string? GetFirstContentText(CallToolResult result)
    {
        if (result.Content != null && result.Content.Count > 0)
        {
            foreach (var block in result.Content)
            {
                if (block is TextContentBlock textBlock)
                {
                    return textBlock.Text;
                }
            }
        }

        return null;
    }

    private static CallToolResult CreateValidationErrorResult(string message)
    {
        var jsonError = JsonSerializer.Serialize(new
        {
            ok = false,
            error = new
            {
                code = "ValidationError",
                message = message
            }
        });

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = jsonError }]
        };
    }

    private static Dictionary<string, Dictionary<string, ParameterInfo>> BuildKnownToolParameters()
    {
        var map = new Dictionary<string, Dictionary<string, ParameterInfo>>(StringComparer.OrdinalIgnoreCase);
        var toolTypes = new[] { typeof(NoteMcpTools), typeof(TrackingMcpTools) };

        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr == null)
                {
                    continue;
                }

                var toolName = attr.Name ?? method.Name;
                var paramDict = method.GetParameters()
                    .Where(p => p.ParameterType != typeof(CancellationToken))
                    .ToDictionary(p => p.Name!, p => p, StringComparer.Ordinal);

                map[toolName] = paramDict;
            }
        }

        return map;
    }
}
