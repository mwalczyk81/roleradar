namespace RoleRadar.Tests.Contract;

using System.Xml;
using Microsoft.OData.Edm.Csdl;
using RoleRadar.Api;
using Xunit;

public sealed class ODataMetadataContractDriftTests
{
    [Fact]
    public void Running_EdmModel_Matches_Committed_Metadata_Xml_Snapshot()
    {
        var model = Program.GetEdmModel();
        var snapshotPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../contracts/odata/metadata.xml"));
        Assert.True(File.Exists(snapshotPath), $"Committed metadata snapshot does not exist at {snapshotPath}");

        var committedXml = File.ReadAllText(snapshotPath);

        using var stringWriter = new StringWriter();
        using (var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { Indent = true, Encoding = System.Text.Encoding.UTF8 }))
        {
            var success = CsdlWriter.TryWriteCsdl(model, xmlWriter, CsdlTarget.OData, out var errors);
            Assert.True(success, $"Failed to write CSDL: {string.Join("; ", errors ?? [])}");
            xmlWriter.Flush();
        }

        var runningXml = stringWriter.ToString();

        // Normalize line endings
        var normalizedCommitted = committedXml.Replace("\r\n", "\n").Trim();
        var normalizedRunning = runningXml.Replace("\r\n", "\n").Trim();

        Assert.Equal(normalizedCommitted, normalizedRunning);
    }
}
