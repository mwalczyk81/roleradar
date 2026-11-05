using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Mcp.Stdio.Tools;
using Xunit;

namespace RoleRadar.Tests.Contract;

/// <summary>Feature 008 phase 2: the posting_transition tool carries eventDate and returns warnings.</summary>
public sealed class PostingTransitionMcpContractTests
{
    private static readonly MethodInfo ToolMethod = typeof(TrackingMcpTools).GetMethod(nameof(TrackingMcpTools.PostingTransitionAsync))!;

    [Fact]
    public void Tool_DeclaresAnOptionalTypedEventDate_WithADescription()
    {
        var parameter = Assert.Single(ToolMethod.GetParameters(), p => p.Name == "eventDate");

        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.True(parameter.IsOptional);
        Assert.Null(parameter.DefaultValue);
        var description = parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.Contains("YYYY-MM-DD", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_DescriptionSaysRejectingAnAppliedPostingAlsoRecordsARejectedEventOnItsApplication()
    {
        var tool = ToolMethod.GetCustomAttribute<McpServerToolAttribute>();
        Assert.Equal("posting_transition", tool!.Name);
        var description = ToolMethod.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

        Assert.Contains("also records a rejected event on its application", description, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCount_StaysTwelve()
    {
        var toolMethods = typeof(TrackingMcpTools).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods())
            .Count(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

        Assert.Equal(12, toolMethods);
    }

    [Fact]
    public async Task Tool_PassesEventDateToTheCommand_AndSerializesWarnings()
    {
        var handler = new CapturingHandler(Result<PostingTransitionResultDto>.Success(new PostingTransitionResultDto(
            PostingId: Guid.NewGuid(),
            PreviousDisposition: "rejected",
            CurrentDisposition: "lead",
            DispositionReason: "Recruiter reopened the role",
            CriterionId: null,
            ResurfacePolicy: "never",
            DecidedAtUtc: null,
            VersionToken: Guid.NewGuid(),
            UpdatedAtUtc: DateTimeOffset.UtcNow)
        {
            Warnings = [TransitionWarnings.Create(TransitionWarnings.ApplicationStillRejected)],
        }));
        var tools = CreateTools(handler);

        var json = await tools.PostingTransitionAsync(
            postingId: Guid.NewGuid(),
            targetDisposition: "rejected",
            versionToken: Guid.NewGuid(),
            reason: "Rejected by email",
            eventDate: "2026-09-15");

        Assert.Equal("2026-09-15", handler.LastCommand!.EventDate);
        using var doc = JsonDocument.Parse(json);
        var warnings = doc.RootElement.GetProperty("data").GetProperty("warnings");
        Assert.Equal(JsonValueKind.Array, warnings.ValueKind);
        var warning = Assert.Single(warnings.EnumerateArray().ToList());
        Assert.Equal("application_still_rejected", warning.GetProperty("code").GetString());
        Assert.Equal("The application is still rejected.", warning.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Tool_ResultWithoutWarnings_SerializesAnEmptyArray()
    {
        var handler = new CapturingHandler(Result<PostingTransitionResultDto>.Success(new PostingTransitionResultDto(
            Guid.NewGuid(), "applied", "rejected", "Rejected by email", null, "never", DateTimeOffset.UtcNow, Guid.NewGuid(), DateTimeOffset.UtcNow)));
        var tools = CreateTools(handler);

        var json = await tools.PostingTransitionAsync(Guid.NewGuid(), "rejected", Guid.NewGuid(), reason: "Rejected by email");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("warnings").GetArrayLength());
        Assert.Null(handler.LastCommand!.EventDate);
    }

    private static TrackingMcpTools CreateTools(ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto> transition) =>
        new(
            new Unused<RecordPostingCommand, PostingRecordResultDto>(),
            transition,
            new Unused<RecordCompanyCommand, CompanyRecordResultDto>(),
            new Unused<TrackerLogCommand, TrackerLogResultDto>(),
            new UnusedQuery<RoleRadar.Application.Tracking.Queries.TrackerQueryRequest, RoleRadar.Application.Tracking.Queries.TrackerQueryResultDto>(),
            new Unused<RecordApplicationCommand, ApplicationRecordResultDto>());

    private sealed class CapturingHandler(Result<PostingTransitionResultDto> result) : ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto>
    {
        public TransitionPostingCommand? LastCommand { get; private set; }

        public Task<Result<PostingTransitionResultDto>> HandleAsync(TransitionPostingCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(result);
        }
    }

    private sealed class Unused<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedQuery<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
