using System.Reflection;

namespace RoleRadar.Api.OpenApi;

/// <summary>
/// Tells whether the process is <c>dotnet-getdocument</c> generating the OpenAPI document at build time
/// (Microsoft.Extensions.ApiDescription.Server, <c>OpenApiGenerateDocumentsOnBuild</c>). The generator launches the API's own
/// entry point with a mock server, so all startup code runs, and Microsoft's guidance for restricting it is to check the
/// entry assembly name, which is <c>GetDocument.Insider</c> there.
/// </summary>
public static class OpenApiDocumentGeneration
{
    public const string GeneratorAssemblyName = "GetDocument.Insider";

    public static bool IsGeneratingDocument(string? entryAssemblyName) =>
        string.Equals(entryAssemblyName, GeneratorAssemblyName, StringComparison.Ordinal);

    public static bool IsGeneratingDocument() => IsGeneratingDocument(Assembly.GetEntryAssembly()?.GetName().Name);
}
