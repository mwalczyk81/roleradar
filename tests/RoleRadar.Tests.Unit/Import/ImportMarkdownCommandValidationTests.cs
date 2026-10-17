using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Import.Commands;
using Xunit;

namespace RoleRadar.Tests.Unit.Import;

public sealed class ImportMarkdownCommandValidationTests
{
    private readonly ImportMarkdownCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenTwoInputPathsResolveToSameFileNameCaseInsensitive_ReturnsValidationErrorNamingCollidingPaths()
    {
        var tempDirA = Path.Combine(Path.GetTempPath(), $"dirA_{Guid.CreateVersion7():N}");
        var tempDirB = Path.Combine(Path.GetTempPath(), $"dirB_{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(tempDirA);
        Directory.CreateDirectory(tempDirB);

        var pathA = Path.Combine(tempDirA, "notes.md");
        var pathB = Path.Combine(tempDirB, "NOTES.MD");

        try
        {
            File.WriteAllText(pathA, "# Heading A\nContent A\n");
            File.WriteAllText(pathB, "# Heading B\nContent B\n");

            var command = new ImportMarkdownCommand(FilePaths: [pathA, pathB]);
            var error = _validator.Validate(command);

            Assert.NotNull(error);
            Assert.Equal(ErrorCode.ValidationError, error.Value.Code);
            Assert.Contains("notes.md", error.Value.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(pathA, error.Value.Message, StringComparison.Ordinal);
            Assert.Contains(pathB, error.Value.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDirA))
            {
                Directory.Delete(tempDirA, true);
            }

            if (Directory.Exists(tempDirB))
            {
                Directory.Delete(tempDirB, true);
            }
        }
    }

    [Fact]
    public void Validate_WhenMultipleFilesProvidedWithSourceId_ReturnsValidationError()
    {
        var tempFile1 = Path.Combine(Path.GetTempPath(), $"file1_{Guid.CreateVersion7():N}.md");
        var tempFile2 = Path.Combine(Path.GetTempPath(), $"file2_{Guid.CreateVersion7():N}.md");

        try
        {
            File.WriteAllText(tempFile1, "# Heading 1\nContent 1\n");
            File.WriteAllText(tempFile2, "# Heading 2\nContent 2\n");

            var command = new ImportMarkdownCommand(
                FilePaths: [tempFile1, tempFile2],
                SourceFileId: "custom-id.md");

            var error = _validator.Validate(command);

            Assert.NotNull(error);
            Assert.Equal(ErrorCode.ValidationError, error.Value.Code);
            Assert.Contains("--source-id", error.Value.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile1))
            {
                File.Delete(tempFile1);
            }

            if (File.Exists(tempFile2))
            {
                File.Delete(tempFile2);
            }
        }
    }

    [Fact]
    public void Validate_WhenSingleFileProvidedWithSourceId_ReturnsNull()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"file_single_{Guid.CreateVersion7():N}.md");

        try
        {
            File.WriteAllText(tempFile, "# Heading Single\nContent\n");

            var command = new ImportMarkdownCommand(
                FilePaths: [tempFile],
                SourceFileId: "custom-single.md");

            var error = _validator.Validate(command);

            Assert.Null(error);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Validate_WhenDistinctFileNamesProvided_ReturnsNull()
    {
        var tempFile1 = Path.Combine(Path.GetTempPath(), $"distinct1_{Guid.CreateVersion7():N}.md");
        var tempFile2 = Path.Combine(Path.GetTempPath(), $"distinct2_{Guid.CreateVersion7():N}.md");

        try
        {
            File.WriteAllText(tempFile1, "# Heading 1\nContent 1\n");
            File.WriteAllText(tempFile2, "# Heading 2\nContent 2\n");

            var command = new ImportMarkdownCommand(FilePaths: [tempFile1, tempFile2]);
            var error = _validator.Validate(command);

            Assert.Null(error);
        }
        finally
        {
            if (File.Exists(tempFile1))
            {
                File.Delete(tempFile1);
            }

            if (File.Exists(tempFile2))
            {
                File.Delete(tempFile2);
            }
        }
    }
}
