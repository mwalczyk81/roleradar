namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerExecutionResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}

public static class ContainerTestHarness
{
    public const string DefaultCliImage = "roleradar-cli:test";
    public const string DefaultMcpImage = "roleradar-mcp:test";

    public static Task<ContainerExecutionResult> RunCliAsync(
        string[] arguments,
        IDictionary<string, string>? environment = null,
        int timeoutSeconds = 30)
    {
        throw new NotImplementedException("ContainerTestHarness not implemented yet.");
    }

    public static Task<ContainerExecutionResult> RunMcpAsync(
        string? inputJsonRpc = null,
        IDictionary<string, string>? environment = null,
        int timeoutSeconds = 30)
    {
        throw new NotImplementedException("ContainerTestHarness not implemented yet.");
    }

    public static Task<ContainerExecutionResult> RunRawAsync(
        string imageTag,
        string[] arguments,
        IDictionary<string, string>? environment = null,
        string? stdinInput = null,
        int timeoutSeconds = 30)
    {
        throw new NotImplementedException("ContainerTestHarness not implemented yet.");
    }
}
