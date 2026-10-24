using System.Text;
using RoleRadar.Infrastructure.Csv;
using Xunit;

namespace RoleRadar.Tests.Unit.Infrastructure;

public sealed class Rfc4180CsvReaderTests
{
    [Fact]
    public void Read_SimpleCsvWithHeader_ReturnsRowsWithCorrectFields()
    {
        var csv = "name,role,status\r\nAlice,Engineer,active\r\nBob,Manager,interviewing";
        using var reader = new StringReader(csv);

        var rows = Rfc4180CsvReader.Read(reader, hasHeader: true).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal("Engineer", rows[0]["role"]);
        Assert.Equal("active", rows[0]["status"]);
        Assert.Equal(2, rows[0].LineNumber); // Row 1 is header, data starts at line 2

        Assert.Equal("Bob", rows[1]["name"]);
        Assert.Equal("Manager", rows[1]["role"]);
        Assert.Equal("interviewing", rows[1]["status"]);
        Assert.Equal(3, rows[1].LineNumber);
    }

    [Fact]
    public void Read_WithUtf8Bom_StripsBomAndParsesHeaderCorrectly()
    {
        var csvWithBom = "\uFEFFid,company\r\n1,Acme Corp";
        using var reader = new StringReader(csvWithBom);

        var rows = Rfc4180CsvReader.Read(reader, hasHeader: true).ToList();

        Assert.Single(rows);
        Assert.True(rows[0].TryGet("id", out var idVal));
        Assert.Equal("1", idVal);
        Assert.Equal("Acme Corp", rows[0]["company"]);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Read_LineEndingVariations_ParsesIdentically(string lineEnding)
    {
        var csv = $"col1,col2{lineEnding}val1,val2{lineEnding}val3,val4";
        using var reader = new StringReader(csv);

        var rows = Rfc4180CsvReader.Read(reader, hasHeader: true).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("val1", rows[0]["col1"]);
        Assert.Equal("val2", rows[0]["col2"]);
        Assert.Equal("val3", rows[1]["col1"]);
        Assert.Equal("val4", rows[1]["col2"]);
    }

    [Fact]
    public void Read_QuotedFieldsWithEmbeddedQuotes_UnescapesQuotes()
    {
        var csv = "title,notes\r\nSenior Dev,\"Said \"\"Hello\"\" to recruiter\"";
        using var reader = new StringReader(csv);

        var rows = Rfc4180CsvReader.Read(reader, hasHeader: true).ToList();

        Assert.Single(rows);
        Assert.Equal("Senior Dev", rows[0]["title"]);
        Assert.Equal("Said \"Hello\" to recruiter", rows[0]["notes"]);
    }

    [Fact]
    public void Read_QuotedFieldsWithEmbeddedNewlines_TracksPhysicalLineNumbers()
    {
        var csv = "id,notes\r\n1,\"Line one\r\nLine two\r\nLine three\"\r\n2,Simple note";
        using var reader = new StringReader(csv);

        var rows = Rfc4180CsvReader.Read(reader, hasHeader: true).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("1", rows[0]["id"]);
        Assert.Equal("Line one\r\nLine two\r\nLine three", rows[0]["notes"]);
        Assert.Equal(2, rows[0].LineNumber);

        Assert.Equal("2", rows[1]["id"]);
        Assert.Equal("Simple note", rows[1]["notes"]);
        Assert.Equal(5, rows[1].LineNumber); // Starts on physical line 5 after the 3-line record
    }

    [Fact]
    public void Read_RaggedRows_ExposesAvailableFieldsWithoutCrashing()
    {
        var csv = "col1,col2,col3\r\ntoo_few\r\nnormal1,normal2,normal3\r\ntoo_many1,too_many2,too_many3,too_many4";
        using var reader = new StringReader(csv);

        var rows = Rfc4180CsvReader.Read(reader, hasHeader: true).ToList();

        Assert.Equal(3, rows.Count);

        // Row 1: only 1 field
        Assert.Equal(1, rows[0].FieldCount);
        Assert.Equal("too_few", rows[0][0]);
        Assert.Null(rows[0]["col2"]);

        // Row 2: 3 fields
        Assert.Equal(3, rows[1].FieldCount);
        Assert.Equal("normal1", rows[1]["col1"]);
        Assert.Equal("normal2", rows[1]["col2"]);
        Assert.Equal("normal3", rows[1]["col3"]);

        // Row 3: 4 fields
        Assert.Equal(4, rows[2].FieldCount);
        Assert.Equal("too_many1", rows[2]["col1"]);
        Assert.Equal("too_many4", rows[2][3]);
    }

    [Fact]
    public void Read_EmptyOrNoTrailingNewline_ParsesCorrectly()
    {
        var csv = "a,b\r\n1,2"; // No trailing newline
        using var reader = new StringReader(csv);

        var rows = Rfc4180CsvReader.Read(reader, hasHeader: true).ToList();

        Assert.Single(rows);
        Assert.Equal("1", rows[0]["a"]);
        Assert.Equal("2", rows[0]["b"]);
    }
}
