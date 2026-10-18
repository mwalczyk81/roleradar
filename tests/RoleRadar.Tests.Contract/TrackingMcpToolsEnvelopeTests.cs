using System.Text.Json;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Common;
using RoleRadar.Mcp.Stdio.Tools;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class TrackingMcpToolsEnvelopeTests
{
    [Fact]
    public async Task CompanyRecord_SuccessAndFailure_ReturnsStandardEnvelope()
    {
        var stubSuccess = new StubCommandHandler<RecordCompanyCommand, CompanyRecordResultDto>(
            Result<CompanyRecordResultDto>.Success(new CompanyRecordResultDto(
                Company: new CompanyRecordSummaryDto(
                    Id: Guid.NewGuid(),
                    Name: "Acme Corp",
                    Description: "Cloud security",
                    OwnershipType: "vc_backed",
                    Status: "target",
                    VersionToken: Guid.NewGuid(),
                    CareerSourceCount: 1),
                Status: "created")));

        var stubFailure = new StubCommandHandler<RecordCompanyCommand, CompanyRecordResultDto>(
            Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Invalid company name."));

        var toolsSuccess = CreateTools(recordCompanyHandler: stubSuccess);
        var toolsFailure = CreateTools(recordCompanyHandler: stubFailure);

        var successJson = await toolsSuccess.CompanyRecordAsync(name: "Acme Corp");
        var failureJson = await toolsFailure.CompanyRecordAsync(name: "");

        using var successDoc = JsonDocument.Parse(successJson);
        Assert.True(successDoc.RootElement.TryGetProperty("ok", out var okSuccess));
        Assert.True(okSuccess.GetBoolean());
        Assert.True(successDoc.RootElement.TryGetProperty("data", out _));

        using var failDoc = JsonDocument.Parse(failureJson);
        Assert.True(failDoc.RootElement.TryGetProperty("ok", out var okFail));
        Assert.False(okFail.GetBoolean());
        Assert.True(failDoc.RootElement.TryGetProperty("error", out var errorProp));
        Assert.Equal("ValidationError", errorProp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostingRecord_SuccessAndFailure_ReturnsStandardEnvelope()
    {
        var stubSuccess = new StubCommandHandler<RecordPostingCommand, PostingRecordResultDto>(
            Result<PostingRecordResultDto>.Success(new PostingRecordResultDto(
                Status: "created",
                Posting: new PostingDto(
                    Id: Guid.NewGuid(),
                    CompanyId: Guid.NewGuid(),
                    Title: "Staff Engineer",
                    NormalizedTitle: "staff engineer",
                    Disposition: "lead",
                    ResurfacePolicy: "never",
                    VersionToken: Guid.NewGuid()))));

        var stubFailure = new StubCommandHandler<RecordPostingCommand, PostingRecordResultDto>(
            Result<PostingRecordResultDto>.Failure(ErrorCode.ValidationError, "CompanyId is required."));

        var toolsSuccess = CreateTools(recordPostingHandler: stubSuccess);
        var toolsFailure = CreateTools(recordPostingHandler: stubFailure);

        var successJson = await toolsSuccess.PostingRecordAsync(companyId: Guid.NewGuid(), title: "Staff Engineer");
        var failureJson = await toolsFailure.PostingRecordAsync(title: "Staff Engineer");

        using var successDoc = JsonDocument.Parse(successJson);
        Assert.True(successDoc.RootElement.TryGetProperty("ok", out var okSuccess));
        Assert.True(okSuccess.GetBoolean());
        Assert.True(successDoc.RootElement.TryGetProperty("data", out _));

        using var failDoc = JsonDocument.Parse(failureJson);
        Assert.True(failDoc.RootElement.TryGetProperty("ok", out var okFail));
        Assert.False(okFail.GetBoolean());
        Assert.True(failDoc.RootElement.TryGetProperty("error", out var errorProp));
        Assert.Equal("ValidationError", errorProp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostingTransition_SuccessAndFailure_ReturnsStandardEnvelope()
    {
        var stubSuccess = new StubCommandHandler<TransitionPostingCommand, PostingTransitionResultDto>(
            Result<PostingTransitionResultDto>.Success(new PostingTransitionResultDto(
                PostingId: Guid.NewGuid(),
                PreviousDisposition: "lead",
                CurrentDisposition: "screened_out",
                DispositionReason: "Compensation mismatch",
                CriterionId: null,
                ResurfacePolicy: "never",
                DecidedAtUtc: DateTimeOffset.UtcNow,
                VersionToken: Guid.NewGuid(),
                UpdatedAtUtc: DateTimeOffset.UtcNow)));

        var stubFailure = new StubCommandHandler<TransitionPostingCommand, PostingTransitionResultDto>(
            Result<PostingTransitionResultDto>.Failure(ErrorCode.InvalidTransition, "Transition not allowed."));

        var toolsSuccess = CreateTools(transitionPostingHandler: stubSuccess);
        var toolsFailure = CreateTools(transitionPostingHandler: stubFailure);

        var successJson = await toolsSuccess.PostingTransitionAsync(postingId: Guid.NewGuid(), targetDisposition: "screened_out", versionToken: Guid.NewGuid());
        var failureJson = await toolsFailure.PostingTransitionAsync(postingId: Guid.NewGuid(), targetDisposition: "screened_out", versionToken: Guid.NewGuid());

        using var successDoc = JsonDocument.Parse(successJson);
        Assert.True(successDoc.RootElement.TryGetProperty("ok", out var okSuccess));
        Assert.True(okSuccess.GetBoolean());
        Assert.True(successDoc.RootElement.TryGetProperty("data", out _));

        using var failDoc = JsonDocument.Parse(failureJson);
        Assert.True(failDoc.RootElement.TryGetProperty("ok", out var okFail));
        Assert.False(okFail.GetBoolean());
        Assert.True(failDoc.RootElement.TryGetProperty("error", out var errorProp));
        Assert.Equal("InvalidTransition", errorProp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TrackerQuery_SuccessAndFailure_ReturnsStandardEnvelope()
    {
        var stubSuccess = new StubQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>(
            Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto(
                Entity: "postings",
                TotalCount: 0,
                Page: 1,
                PageSize: 50,
                Items: [])));

        var stubFailure = new StubQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>(
            Result<TrackerQueryResultDto>.Failure(ErrorCode.ValidationError, "Entity is invalid."));

        var toolsSuccess = CreateTools(trackerQueryHandler: stubSuccess);
        var toolsFailure = CreateTools(trackerQueryHandler: stubFailure);

        var successJson = await toolsSuccess.TrackerQueryAsync(entity: "postings");
        var failureJson = await toolsFailure.TrackerQueryAsync(entity: "invalid");

        using var successDoc = JsonDocument.Parse(successJson);
        Assert.True(successDoc.RootElement.TryGetProperty("ok", out var okSuccess));
        Assert.True(okSuccess.GetBoolean());
        Assert.True(successDoc.RootElement.TryGetProperty("data", out _));

        using var failDoc = JsonDocument.Parse(failureJson);
        Assert.True(failDoc.RootElement.TryGetProperty("ok", out var okFail));
        Assert.False(okFail.GetBoolean());
        Assert.True(failDoc.RootElement.TryGetProperty("error", out var errorProp));
        Assert.Equal("ValidationError", errorProp.GetProperty("code").GetString());
    }

    private static TrackingMcpTools CreateTools(
        ICommandHandler<RecordPostingCommand, PostingRecordResultDto>? recordPostingHandler = null,
        ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto>? transitionPostingHandler = null,
        ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>? recordCompanyHandler = null,
        IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>? trackerQueryHandler = null)
    {
        return new TrackingMcpTools(
            recordPostingHandler ?? new StubCommandHandler<RecordPostingCommand, PostingRecordResultDto>(Result<PostingRecordResultDto>.Failure(ErrorCode.ValidationError, "")),
            transitionPostingHandler ?? new StubCommandHandler<TransitionPostingCommand, PostingTransitionResultDto>(Result<PostingTransitionResultDto>.Failure(ErrorCode.ValidationError, "")),
            recordCompanyHandler ?? new StubCommandHandler<RecordCompanyCommand, CompanyRecordResultDto>(Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "")),
            trackerQueryHandler ?? new StubQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>(Result<TrackerQueryResultDto>.Failure(ErrorCode.ValidationError, "")));
    }

    private sealed class StubCommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        private readonly Result<TResponse> _result;
        public StubCommandHandler(Result<TResponse> result) => _result = result;
        public Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class StubQueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        private readonly Result<TResponse> _result;
        public StubQueryHandler(Result<TResponse> result) => _result = result;
        public Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}
