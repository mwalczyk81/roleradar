using System.Text.Json;
using ModelContextProtocol.Protocol;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Mcp.Stdio.Filters;
using RoleRadar.Mcp.Stdio.Tools;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class PostingRecordSignalsContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;
    private RecordPostingCommandHandler _handler = null!;
    private TrackingMcpTools _tools = null!;

    public PostingRecordSignalsContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_signals_mcp_contract");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
        _companyRepo = new MongoCompanyRepository(_client, _database);
        _postingRepo = new MongoPostingRepository(_client, _database);
        _context = new TrackingTransactionContextReal(_companyRepo, _postingRepo);
        var clock = new SystemUtcClock();
        _handler = new RecordPostingCommandHandler(_context, clock);
        _tools = new TrackingMcpTools(
            _handler,
            new StubCommandHandler<TransitionPostingCommand, PostingTransitionResultDto>(),
            new StubCommandHandler<RecordCompanyCommand, CompanyRecordResultDto>(),
            new StubCommandHandler<TrackerLogCommand, TrackerLogResultDto>(),
            new StubQueryHandler<RoleRadar.Application.Tracking.Queries.TrackerQueryRequest, RoleRadar.Application.Tracking.Queries.TrackerQueryResultDto>(),
            new StubCommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null && !string.IsNullOrWhiteSpace(_databaseName))
            {
                await _client.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    [Fact]
    public void SignalInputDtoConverter_DeserializesShorthandAndObjectForms()
    {
        var json = """
            [
                "no_salary",
                "repost",
                "parallel_posting",
                "gone",
                "phantom",
                "deadline:2026-10-31",
                {"signalType": "no_salary"},
                {"signalType": "deadline", "deadlineDate": "2026-11-15", "evidence": "observed in listing"}
            ]
            """;

        var signals = JsonSerializer.Deserialize<SignalInputDto[]>(json);
        Assert.NotNull(signals);
        Assert.Equal(8, signals.Length);

        // Shorthand checks
        Assert.Equal("no_salary", signals[0].SignalType);
        Assert.Equal("reported by agent", signals[0].Evidence);
        Assert.Null(signals[0].DeadlineDate);

        Assert.Equal("repost", signals[1].SignalType);
        Assert.Equal("reported by agent", signals[1].Evidence);

        Assert.Equal("parallel_posting", signals[2].SignalType);
        Assert.Equal("reported by agent", signals[2].Evidence);

        Assert.Equal("gone", signals[3].SignalType);
        Assert.Equal("reported by agent", signals[3].Evidence);

        Assert.Equal("phantom", signals[4].SignalType);
        Assert.Equal("reported by agent", signals[4].Evidence);

        Assert.Equal("deadline", signals[5].SignalType);
        Assert.Equal("reported by agent", signals[5].Evidence);
        Assert.Equal("2026-10-31", signals[5].DeadlineDate);

        // Object checks
        Assert.Equal("no_salary", signals[6].SignalType);
        Assert.Equal("reported by agent", signals[6].Evidence);

        Assert.Equal("deadline", signals[7].SignalType);
        Assert.Equal("observed in listing", signals[7].Evidence);
        Assert.Equal("2026-11-15", signals[7].DeadlineDate);
    }

    [Fact]
    public async Task PostingRecord_WithStringShorthand_PersistsSignalsWithDefaultEvidenceAndParsedDeadline()
    {
        var ownerId = OwnerId.Default.Value;
        var company = Company.Create(ownerId, "Signals Shorthand Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var signalsJson = """["no_salary", "deadline:2026-10-31"]""";
        var signals = JsonSerializer.Deserialize<SignalInputDto[]>(signalsJson);

        var resultJson = await _tools.PostingRecordAsync(
            companyId: company.Id,
            title: "Senior Backend Engineer",
            sourceKind: "ats",
            signals: signals);

        using var doc = JsonDocument.Parse(resultJson);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var postingId = doc.RootElement.GetProperty("data").GetProperty("posting").GetProperty("id").GetGuid();

        var retrievedSignals = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.True(retrievedSignals.IsSuccess);
        Assert.Equal(2, retrievedSignals.Value!.Count);

        var noSalarySig = Assert.Single(retrievedSignals.Value, s => s.SignalType == SignalType.NoSalary);
        Assert.Equal("reported by agent", noSalarySig.Evidence);
        Assert.Null(noSalarySig.DeadlineDate);

        var deadlineSig = Assert.Single(retrievedSignals.Value, s => s.SignalType == SignalType.Deadline);
        Assert.Equal("reported by agent", deadlineSig.Evidence);
        Assert.Equal("2026-10-31", deadlineSig.DeadlineDate);
    }

    [Fact]
    public async Task PostingRecord_WithObjectForm_PersistsSignals()
    {
        var ownerId = OwnerId.Default.Value;
        var company = Company.Create(ownerId, "Signals Object Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var signalsJson = """[{"signalType": "repost", "evidence": "observed on company careers page"}]""";
        var signals = JsonSerializer.Deserialize<SignalInputDto[]>(signalsJson);

        var resultJson = await _tools.PostingRecordAsync(
            companyId: company.Id,
            title: "Staff Systems Engineer",
            sourceKind: "ats",
            signals: signals);

        using var doc = JsonDocument.Parse(resultJson);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var postingId = doc.RootElement.GetProperty("data").GetProperty("posting").GetProperty("id").GetGuid();

        var retrievedSignals = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.True(retrievedSignals.IsSuccess);
        var repostSig = Assert.Single(retrievedSignals.Value!, s => s.SignalType == SignalType.Repost);
        Assert.Equal("observed on company careers page", repostSig.Evidence);
    }

    [Fact]
    public async Task PostingRecord_WithMixedSignalFormats_PersistsBothForms()
    {
        var ownerId = OwnerId.Default.Value;
        var company = Company.Create(ownerId, "Signals Mixed Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var signalsJson = """["phantom", {"signalType": "gone", "evidence": "404 error page"}]""";
        var signals = JsonSerializer.Deserialize<SignalInputDto[]>(signalsJson);

        var resultJson = await _tools.PostingRecordAsync(
            companyId: company.Id,
            title: "Platform Architect",
            sourceKind: "ats",
            signals: signals);

        using var doc = JsonDocument.Parse(resultJson);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var postingId = doc.RootElement.GetProperty("data").GetProperty("posting").GetProperty("id").GetGuid();

        var retrievedSignals = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.True(retrievedSignals.IsSuccess);
        Assert.Equal(2, retrievedSignals.Value!.Count);

        var phantomSig = Assert.Single(retrievedSignals.Value, s => s.SignalType == SignalType.Phantom);
        Assert.Equal("reported by agent", phantomSig.Evidence);

        var goneSig = Assert.Single(retrievedSignals.Value, s => s.SignalType == SignalType.Gone);
        Assert.Equal("404 error page", goneSig.Evidence);
    }

    [Fact]
    public async Task PostingRecord_WithInvalidSignalValue_ReturnsValidationError()
    {
        var ownerId = OwnerId.Default.Value;
        var company = Company.Create(ownerId, "Signals Invalid Value Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var signalsJson = """["not_a_valid_signal"]""";
        var signals = JsonSerializer.Deserialize<SignalInputDto[]>(signalsJson);

        var resultJson = await _tools.PostingRecordAsync(
            companyId: company.Id,
            title: "Data Engineer",
            sourceKind: "ats",
            signals: signals);

        using var doc = JsonDocument.Parse(resultJson);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        Assert.Contains("Invalid signalType 'not_a_valid_signal'", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostingRecord_WithMalformedDeadlineDate_ReturnsValidationError()
    {
        var ownerId = OwnerId.Default.Value;
        var company = Company.Create(ownerId, "Signals Malformed Date Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        // String shorthand with invalid date format
        var shorthandJson = """["deadline:2026/10/31"]""";
        var shorthandSignals = JsonSerializer.Deserialize<SignalInputDto[]>(shorthandJson);

        var shorthandResult = await _tools.PostingRecordAsync(
            companyId: company.Id,
            title: "ML Engineer",
            sourceKind: "ats",
            signals: shorthandSignals);

        using var shortDoc = JsonDocument.Parse(shorthandResult);
        Assert.False(shortDoc.RootElement.GetProperty("ok").GetBoolean());
        var shortError = shortDoc.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", shortError.GetProperty("code").GetString());
        Assert.Equal("Deadline date must be in YYYY-MM-DD format.", shortError.GetProperty("message").GetString());

        // Object form with invalid date format
        var objectJson = """[{"signalType": "deadline", "deadlineDate": "invalid-date"}]""";
        var objectSignals = JsonSerializer.Deserialize<SignalInputDto[]>(objectJson);

        var objectResult = await _tools.PostingRecordAsync(
            companyId: company.Id,
            title: "ML Engineer",
            sourceKind: "ats",
            signals: objectSignals);

        using var objDoc = JsonDocument.Parse(objectResult);
        Assert.False(objDoc.RootElement.GetProperty("ok").GetBoolean());
        var objError = objDoc.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", objError.GetProperty("code").GetString());
        Assert.Equal("Deadline date must be in YYYY-MM-DD format.", objError.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FilterPipeline_WithSignalStringShorthand_CoercesAndExecutesSuccessfully()
    {
        var ownerId = OwnerId.Default.Value;
        var company = Company.Create(ownerId, "Pipeline Signals Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var argsJson = $$"""
            {
                "companyId": "{{company.Id}}",
                "title": "Principal Architect",
                "signals": ["no_salary", "deadline:2026-10-31"]
            }
            """;
        using var argsDoc = JsonDocument.Parse(argsJson);
        var args = argsDoc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        var requestParams = new CallToolRequestParams
        {
            Name = "posting_record",
            Arguments = args
        };

        var result = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, async p =>
        {
            // Simulate MCP SDK binding args to method invocation
            var cid = p.Arguments!.TryGetValue("companyId", out var cElem) ? cElem.GetGuid() : (Guid?)null;
            var title = p.Arguments.TryGetValue("title", out var tElem) ? tElem.GetString() : null;
            SignalInputDto[]? sigs = null;
            if (p.Arguments.TryGetValue("signals", out var sElem))
            {
                sigs = JsonSerializer.Deserialize<SignalInputDto[]>(sElem.GetRawText());
            }

            var toolJson = await _tools.PostingRecordAsync(companyId: cid, title: title, signals: sigs);
            return new CallToolResult { Content = [new TextContentBlock { Text = toolJson }] };
        });

        Assert.False(result.IsError == true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content!)).Text;
        using var doc = JsonDocument.Parse(text!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var postingId = doc.RootElement.GetProperty("data").GetProperty("posting").GetProperty("id").GetGuid();

        var retrievedSignals = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.True(retrievedSignals.IsSuccess);
        Assert.Equal(2, retrievedSignals.Value!.Count);
        Assert.Contains(retrievedSignals.Value, s => s.SignalType == SignalType.NoSalary && s.Evidence == "reported by agent");
        Assert.Contains(retrievedSignals.Value, s => s.SignalType == SignalType.Deadline && s.DeadlineDate == "2026-10-31");
    }

    [Fact]
    public async Task FilterPipeline_WithUnknownArgument_ReturnsValidationError()
    {
        var argsJson = """
            {
                "companyId": "00000000-0000-0000-0000-000000000001",
                "title": "Engineer",
                "bogusArgument": "unexpected"
            }
            """;
        using var argsDoc = JsonDocument.Parse(argsJson);
        var args = argsDoc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        var requestParams = new CallToolRequestParams
        {
            Name = "posting_record",
            Arguments = args
        };

        var result = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, p =>
            ValueTask.FromResult(new CallToolResult()));

        Assert.False(result.IsError == true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content!)).Text;
        using var doc = JsonDocument.Parse(text!);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        Assert.Equal("Unknown argument(s): 'bogusArgument'.", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("posting_record", "signals", "123", "Invalid argument for 'signals': expected array of signal strings or objects.")]
    [InlineData("posting_record", "companyId", "\"not-a-uuid\"", "Invalid argument for 'companyId': expected UUID string.")]
    [InlineData("posting_record", "score", "\"not-a-number\"", "Invalid argument for 'score': expected number.")]
    [InlineData("posting_record", "signals", "[123]", "Invalid argument for 'signals': expected array of signal strings or objects.")]
    [InlineData("note_create", "tags", "\"single-string\"", "Invalid argument for 'tags': expected array of strings.")]
    [InlineData("note_get", "id", "\"not-a-uuid\"", "Invalid argument for 'id': expected UUID string.")]
    [InlineData("application_record", "overrideDecided", "\"not-a-bool\"", "Invalid argument for 'overrideDecided': expected boolean.")]
    public async Task FilterPipeline_WithMisshapenArgument_ReturnsValidationError(
        string toolName,
        string paramName,
        string rawJsonValue,
        string expectedMessage)
    {
        using var valDoc = JsonDocument.Parse(rawJsonValue);
        var args = new Dictionary<string, JsonElement>
        {
            [paramName] = valDoc.RootElement.Clone()
        };

        var requestParams = new CallToolRequestParams
        {
            Name = toolName,
            Arguments = args
        };

        var result = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, p =>
            ValueTask.FromResult(new CallToolResult()));

        Assert.False(result.IsError == true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content!)).Text;
        using var doc = JsonDocument.Parse(text!);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        Assert.Equal(expectedMessage, error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FilterPipeline_InterceptsGenericSdkError_AndReturnsStructuredEnvelope()
    {
        var requestParams = new CallToolRequestParams
        {
            Name = "posting_record",
            Arguments = new Dictionary<string, JsonElement>()
        };

        // Simulate ModelContextProtocol SDK generic failure response
        var result = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, p =>
            ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "An error occurred invoking 'posting_record'" }]
            }));

        Assert.False(result.IsError == true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content!)).Text;
        using var doc = JsonDocument.Parse(text!);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        Assert.Equal("Invalid argument for 'posting_record': invocation failure.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FilterPipeline_CatchesUnexpectedException_AndReturnsStructuredEnvelopeWithoutStackTrace()
    {
        var requestParams = new CallToolRequestParams
        {
            Name = "posting_record",
            Arguments = new Dictionary<string, JsonElement>()
        };

        var result = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, p =>
            throw new InvalidOperationException("Sensitive internal database detail that must never leak"));

        Assert.False(result.IsError == true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content!)).Text;
        using var doc = JsonDocument.Parse(text!);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        Assert.Equal("Invalid argument for 'posting_record'.", error.GetProperty("message").GetString());
        // Verify no stack trace or sensitive message leaked
        Assert.DoesNotContain("Sensitive internal", text);
        Assert.DoesNotContain("StackTrace", text);
    }

    private sealed class TrackingTransactionContextReal : ITrackingTransactionContext
    {
        public TrackingTransactionContextReal(ICompanyRepository companyRepo, IPostingRepository postingRepo)
        {
            Companies = companyRepo;
            Postings = postingRepo;
        }

        public ICompanyRepository Companies { get; }
        public IPostingRepository Postings { get; }
        public ICriterionRepository Criteria => throw new NotImplementedException();
        public IApplicationRepository Applications => throw new NotImplementedException();
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();
        public List<Domain.Audit.AuditLogEntry> AuditEntries { get; } = new();

        public Task AppendAuditEntryAsync(Domain.Audit.AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            AuditEntries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class StubCommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TResponse>.Failure(ErrorCode.ValidationError, "Stub"));
    }

    private sealed class StubQueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TResponse>.Failure(ErrorCode.ValidationError, "Stub"));
    }
}
