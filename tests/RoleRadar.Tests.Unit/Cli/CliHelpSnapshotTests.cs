using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Cli.Commands;
using Xunit;

namespace RoleRadar.Tests.Unit.Cli;

public sealed class CliHelpSnapshotTests
{
    private readonly RootCommand _rootCommand;

    public CliHelpSnapshotTests()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        _rootCommand = new RootCommand("RoleRadar local CLI");
        _rootCommand.Add(StatusCommand.Create(services));
        _rootCommand.Add(ImportCommand.Create(services));
        _rootCommand.Add(BackupCommand.Create(services));
        _rootCommand.Add(RestoreCommand.Create(services));
        _rootCommand.Add(WipeCommand.Create(services));
        _rootCommand.Add(ContactCommand.Create(services));
        _rootCommand.Add(TrackerImportCommand.Create(services));
        _rootCommand.Add(VerifyCommand.Create(services));
    }

    [Fact]
    public void RootCommand_ContainsTrackerImport_WithDryRunDefaultDocumented()
    {
        var trackerImportCmd = _rootCommand.Subcommands.FirstOrDefault(c => c.Name == "tracker-import");
        Assert.NotNull(trackerImportCmd);
        Assert.Contains("previews by default", trackerImportCmd.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--apply to write changes", trackerImportCmd.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unlike import which writes by default", trackerImportCmd.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RootCommand_ContainsImport_WithWritesByDefaultDocumented()
    {
        var importCmd = _rootCommand.Subcommands.FirstOrDefault(c => c.Name == "import");
        Assert.NotNull(importCmd);
        Assert.Contains("writes by default", importCmd.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--dry-run to preview without writing", importCmd.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unlike tracker-import which previews by default", importCmd.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrackerImportCommand_DocumentsOptions_AndArguments()
    {
        var trackerImportCmd = _rootCommand.Subcommands.FirstOrDefault(c => c.Name == "tracker-import");
        Assert.NotNull(trackerImportCmd);

        Assert.Single(trackerImportCmd.Arguments);
        Assert.Equal("bundle-directory", trackerImportCmd.Arguments[0].Name);

        var applyOption = trackerImportCmd.Options.FirstOrDefault(o => o.Name == "--apply");
        Assert.NotNull(applyOption);
        Assert.Contains("dry-run preview is the default", applyOption.Description, StringComparison.OrdinalIgnoreCase);

        var reportOption = trackerImportCmd.Options.FirstOrDefault(o => o.Name == "--report");
        Assert.NotNull(reportOption);
        Assert.Contains("refuses paths inside a git working tree", reportOption.Description, StringComparison.OrdinalIgnoreCase);

        var defaultDateOption = trackerImportCmd.Options.FirstOrDefault(o => o.Name == "--default-date");
        Assert.NotNull(defaultDateOption);
        Assert.Contains("YYYY-MM-DD", defaultDateOption.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RootHelp_SnapshotText_ContainsBothCommandsAndDescriptions()
    {
        using var sw = new StringWriter();
        var origOut = Console.Out;
        try
        {
            Console.SetOut(sw);
            var parseResult = _rootCommand.Parse("--help");
            parseResult.Invoke();
        }
        finally
        {
            Console.SetOut(origOut);
        }

        var help = sw.ToString();
        Assert.Contains("tracker-import", help);
        Assert.Contains("previews by default", help);
        Assert.Contains("import", help);
        Assert.Contains("writes by default", help);
    }
}
