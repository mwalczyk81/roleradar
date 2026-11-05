namespace RoleRadar.Tests.Contract;

using System.Net;
using System.Xml;
using System.Xml.Linq;
using Microsoft.OData.Edm.Csdl;
using RoleRadar.Api;
using RoleRadar.Tests.Contract.Api;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

/// <summary>
/// T064: the committed <c>contracts/odata/metadata.xml</c> is the OData contract. The EDM model the API builds and
/// the <c>/odata/$metadata</c> document a running API serves must both equal it, so the file can never drift from
/// the service (regenerate it from the model; never hand-edit).
/// </summary>
public sealed class ODataMetadataDriftTests(SharedMongoFixture sharedMongo)
{
    private static string SnapshotPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../contracts/odata/metadata.xml"));

    [Fact]
    public void Running_EdmModel_Matches_Committed_Metadata_Xml_Snapshot()
    {
        var model = Program.GetEdmModel();

        using var stringWriter = new StringWriter();
        using (var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { Indent = true, Encoding = System.Text.Encoding.UTF8 }))
        {
            var success = CsdlWriter.TryWriteCsdl(model, xmlWriter, CsdlTarget.OData, out var errors);
            Assert.True(success, $"Failed to write CSDL: {string.Join("; ", errors ?? [])}");
            xmlWriter.Flush();
        }

        var runningXml = stringWriter.ToString();

        // Regenerate the committed snapshot from the model (never hand-edit it): UPDATE_SNAPSHOTS=1 dotnet test ...
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(SnapshotPath, runningXml.Replace("\r\n", "\n").TrimEnd() + "\n");
        }

        Assert.True(File.Exists(SnapshotPath), $"Committed metadata snapshot does not exist at {SnapshotPath}");
        var committedXml = File.ReadAllText(SnapshotPath);

        // Normalize line endings
        var normalizedCommitted = committedXml.Replace("\r\n", "\n").Trim();
        var normalizedRunning = runningXml.Replace("\r\n", "\n").Trim();

        Assert.Equal(normalizedCommitted, normalizedRunning);
    }

    [Fact]
    public async Task Served_Odata_Metadata_Document_Equals_Committed_Snapshot()
    {
        await using var host = await ApiContractTestHost.StartAsync(sharedMongo, "rrtest_api_odata_metadata");

        using var response = await host.Client.GetAsync("/odata/$metadata");
        var served = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"/odata/$metadata returned {(int)response.StatusCode}: {served}");

        var committed = XDocument.Parse(File.ReadAllText(SnapshotPath));
        var running = XDocument.Parse(served);

        // Compare the parsed documents so serializer whitespace and the XML declaration cannot cause a false drift.
        Assert.True(
            XNode.DeepEquals(committed.Root, running.Root),
            "The served /odata/$metadata differs from contracts/odata/metadata.xml. Regenerate the snapshot from the EDM model.");
    }
}
