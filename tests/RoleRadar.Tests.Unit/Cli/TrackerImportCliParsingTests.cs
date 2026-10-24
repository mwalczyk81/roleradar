using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Cli.Commands;
using Xunit;

namespace RoleRadar.Tests.Unit.Cli;

public sealed class TrackerImportCliParsingTests
{
    private readonly Command _command;

    public TrackerImportCliParsingTests()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        _command = TrackerImportCommand.Create(services);
    }

    [Fact]
    public void Parse_DefaultInvocation_DefaultsToDryRun()
    {
        var parseResult = _command.Parse("C:\\fictional\\bundle");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("C:\\fictional\\bundle", parseResult.GetValue<string>((Argument<string>)_command.Arguments[0]));
        Assert.False(parseResult.GetValue<bool>("--apply"));
    }

    [Fact]
    public void Parse_WithApplyFlag_EnablesApply()
    {
        var parseResult = _command.Parse("C:\\fictional\\bundle --apply");

        Assert.Empty(parseResult.Errors);
        Assert.True(parseResult.GetValue<bool>("--apply"));
    }

    [Fact]
    public void Parse_WithValidDefaultDate_ParsesDateSuccessfully()
    {
        var parseResult = _command.Parse("C:\\fictional\\bundle --default-date 2026-10-24");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("2026-10-24", parseResult.GetValue<string?>("--default-date"));
    }

    [Fact]
    public void Parse_WithReportOption_CapturesReportPath()
    {
        var parseResult = _command.Parse("C:\\fictional\\bundle --report C:\\temp\\report.json");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("C:\\temp\\report.json", parseResult.GetValue<string?>("--report"));
    }

    [Fact]
    public void Parse_MissingBundleArgument_ReportsError()
    {
        var parseResult = _command.Parse("");

        Assert.NotEmpty(parseResult.Errors);
    }
}
