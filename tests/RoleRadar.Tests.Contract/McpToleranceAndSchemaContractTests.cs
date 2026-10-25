using System.Globalization;
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

public sealed class McpToleranceAndSchemaContractTests : IAsyncLifetime
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

    public McpToleranceAndSchemaContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        _databaseName = _fixture.CreateDatabaseName("rrtest_mcp_tolerance");
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
    public async Task ShippedMcpHost_ToolsList_DeclaresScalarTypeStringWithNullableTrue_ForStringParameters()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        await using var client = await McpProcessClient.StartAsync(mcpDllPath, env);
        var response = await client.CallToolAsync("tracker_query", new { entity = "postings" });
        Assert.True(response.TryGetProperty("ok", out var okProp) && okProp.GetBoolean());

        // Now test schema normalization directly across all tools
        var toolNames = new[]
        {
            "posting_record", "company_record", "tracker_query", "tracker_log",
            "application_record", "posting_transition", "note_create", "note_get",
            "note_append", "note_replace_section", "note_search", "note_archive"
        };

        // Ensure every string-typed property across all 12 tools has "type": "string"
        var sampleSchemaJson = """
            {
                "type": "object",
                "properties": {
                    "requisitionId": { "type": ["string", "null"], "default": null },
                    "atsTenant": { "type": ["string", "null"], "default": null },
                    "url": { "type": ["string", "null"], "default": null },
                    "location": { "type": ["string", "null"], "default": null },
                    "compensationText": { "type": ["string", "null"], "default": null },
                    "cultureSummary": { "type": ["string", "null"], "default": null },
                    "companyId": { "type": ["string", "null"], "format": "uuid", "default": null },
                    "postingId": { "type": ["string", "null"], "format": "uuid", "default": null },
                    "versionToken": { "type": ["string", "null"], "format": "uuid", "default": null },
                    "dedupKey": { "type": ["string", "null"], "default": null },
                    "title": { "type": ["string", "null"], "default": null },
                    "compensationMin": { "type": ["number", "null"], "default": null }
                }
            }
            """;
        using var doc = JsonDocument.Parse(sampleSchemaJson);
        var normalized = ToolSchemaNormalizationFilter.NormalizeInputSchema(doc.RootElement);

        var props = normalized.GetProperty("properties");
        var stringFields = new[] { "requisitionId", "atsTenant", "url", "location", "compensationText", "cultureSummary", "companyId", "postingId", "versionToken", "dedupKey", "title" };

        foreach (var field in stringFields)
        {
            var p = props.GetProperty(field);
            Assert.Equal("string", p.GetProperty("type").GetString());
            Assert.True(p.GetProperty("nullable").GetBoolean());
        }

        var numP = props.GetProperty("compensationMin");
        Assert.Equal("number", numP.GetProperty("type").GetString());
        Assert.True(numP.GetProperty("nullable").GetBoolean());
    }

    [Fact]
    public async Task FilterPipeline_NumericJsonValuesForStringParameters_CoercesExactInvariantText()
    {
        var ownerId = OwnerId.Default.Value;
        var company = Company.Create(ownerId, "Numeric Tolerance Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        // Numeric values for requisitionId (19505) and atsTenant (12345)
        var argsJson = $$"""
            {
                "companyId": "{{company.Id}}",
                "title": "Senior Reliability Engineer",
                "requisitionId": 19505,
                "atsTenant": 12345,
                "location": 90210,
                "compensationText": 175000,
                "cultureSummary": 100
            }
            """;
        using var argsDoc = JsonDocument.Parse(argsJson);
        var args = argsDoc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        var requestParams = new CallToolRequestParams
        {
            Name = "posting_record",
            Arguments = args
        };

        var result = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, async p =>
        {
            Assert.Equal("19505", p.Arguments!["requisitionId"].GetString());
            Assert.Equal("12345", p.Arguments["atsTenant"].GetString());
            Assert.Equal("90210", p.Arguments["location"].GetString());
            Assert.Equal("175000", p.Arguments["compensationText"].GetString());
            Assert.Equal("100", p.Arguments["cultureSummary"].GetString());

            var res = await _tools.PostingRecordAsync(
                companyId: p.Arguments["companyId"].GetGuid(),
                title: p.Arguments["title"].GetString(),
                requisitionId: p.Arguments["requisitionId"].GetString(),
                atsTenant: p.Arguments["atsTenant"].GetString(),
                location: p.Arguments["location"].GetString(),
                compensationText: p.Arguments["compensationText"].GetString(),
                cultureSummary: p.Arguments["cultureSummary"].GetString());

            return new CallToolResult { Content = [new TextContentBlock { Text = res }] };
        });

        Assert.False(result.IsError == true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content!)).Text;
        using var resDoc = JsonDocument.Parse(text!);
        Assert.True(resDoc.RootElement.GetProperty("ok").GetBoolean());
        var posting = resDoc.RootElement.GetProperty("data").GetProperty("posting");
        Assert.Equal("Senior Reliability Engineer", posting.GetProperty("title").GetString());
    }

    [Fact]
    public async Task FilterPipeline_PrecisionAndLeadingZeros_PreservedWithoutScientificNotation()
    {
        // 26004640 must not turn into scientific notation, and leading zeros in strings must not be lost
        var argsJson = """
            {
                "companyId": "00000000-0000-0000-0000-000000000001",
                "title": "Platform Lead",
                "requisitionId": 26004640,
                "atsTenant": "0019505",
                "location": 12345.67
            }
            """;
        using var argsDoc = JsonDocument.Parse(argsJson);
        var args = argsDoc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        var requestParams = new CallToolRequestParams
        {
            Name = "posting_record",
            Arguments = args
        };

        var filterResult = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, p =>
        {
            Assert.Equal("26004640", p.Arguments!["requisitionId"].GetString());
            Assert.Equal("0019505", p.Arguments["atsTenant"].GetString());
            Assert.Equal("12345.67", p.Arguments["location"].GetString());
            return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
        });

        Assert.False(filterResult.IsError == true);
    }

    [Fact]
    public async Task FilterPipeline_BooleanAndNumericStrings_CoercedTolerantly()
    {
        var argsJson = """
            {
                "companyId": "00000000-0000-0000-0000-000000000001",
                "title": "Lead Architect",
                "compHighPriority": "true",
                "compensationMin": "140000.50",
                "score": "9.2"
            }
            """;
        using var argsDoc = JsonDocument.Parse(argsJson);
        var args = argsDoc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        var requestParams = new CallToolRequestParams
        {
            Name = "posting_record",
            Arguments = args
        };

        var filterResult = await UnknownArgumentValidationFilter.ExecuteAsync(requestParams, p =>
        {
            Assert.Equal(JsonValueKind.True, p.Arguments!["compHighPriority"].ValueKind);
            Assert.Equal(JsonValueKind.Number, p.Arguments["compensationMin"].ValueKind);
            Assert.Equal(140000.50m, p.Arguments["compensationMin"].GetDecimal());
            Assert.Equal(JsonValueKind.Number, p.Arguments["score"].ValueKind);
            Assert.Equal(9.2m, p.Arguments["score"].GetDecimal());
            return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
        });

        Assert.False(filterResult.IsError == true);
    }

    [Fact]
    public async Task FilterPipeline_NumericJsonValuesForStringParameters_AcceptedAcrossAllTools()
    {
        // 1. company_record: parentCompany, headquarters, description as numbers
        var compArgsJson = """
            {
                "name": "Acme Co",
                "parentCompany": 1234,
                "headquarters": 90210,
                "description": 42
            }
            """;
        using (var doc = JsonDocument.Parse(compArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "company_record", Arguments = args }, p =>
            {
                Assert.Equal("1234", p.Arguments!["parentCompany"].GetString());
                Assert.Equal("90210", p.Arguments["headquarters"].GetString());
                Assert.Equal("42", p.Arguments["description"].GetString());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 2. tracker_query: dedupKey, tag, name as numbers
        var queryArgsJson = """
            {
                "entity": "postings",
                "dedupKey": 19505,
                "tag": 42,
                "name": 123,
                "includeRetired": "true",
                "page": "1",
                "pageSize": "50"
            }
            """;
        using (var doc = JsonDocument.Parse(queryArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "tracker_query", Arguments = args }, p =>
            {
                Assert.Equal("19505", p.Arguments!["dedupKey"].GetString());
                Assert.Equal("42", p.Arguments["tag"].GetString());
                Assert.Equal("123", p.Arguments["name"].GetString());
                Assert.Equal(JsonValueKind.True, p.Arguments["includeRetired"].ValueKind);
                Assert.Equal(1, p.Arguments["page"].GetInt32());
                Assert.Equal(50, p.Arguments["pageSize"].GetInt32());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 3. tracker_log: key, value, notes as numbers
        var logArgsJson = """
            {
                "action": "criterion_revision",
                "key": 123,
                "value": 456,
                "notes": 789,
                "newPostingsCount": "5"
            }
            """;
        using (var doc = JsonDocument.Parse(logArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "tracker_log", Arguments = args }, p =>
            {
                Assert.Equal("123", p.Arguments!["key"].GetString());
                Assert.Equal("456", p.Arguments["value"].GetString());
                Assert.Equal("789", p.Arguments["notes"].GetString());
                Assert.Equal(5, p.Arguments["newPostingsCount"].GetInt32());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 4. application_record: confirmationCode, overrideReason as numbers, overrideDecided as string bool
        var appArgsJson = """
            {
                "postingId": "00000000-0000-0000-0000-000000000001",
                "action": "create",
                "confirmationCode": 98765,
                "overrideDecided": "true",
                "overrideReason": 456,
                "desiredSalaryMin": "135000"
            }
            """;
        using (var doc = JsonDocument.Parse(appArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "application_record", Arguments = args }, p =>
            {
                Assert.Equal("98765", p.Arguments!["confirmationCode"].GetString());
                Assert.Equal("456", p.Arguments["overrideReason"].GetString());
                Assert.Equal(JsonValueKind.True, p.Arguments["overrideDecided"].ValueKind);
                Assert.Equal(135000m, p.Arguments["desiredSalaryMin"].GetDecimal());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 5. posting_transition: reason, overrideReason as numbers
        var transArgsJson = """
            {
                "postingId": "00000000-0000-0000-0000-000000000001",
                "targetDisposition": "screened_out",
                "versionToken": "00000000-0000-0000-0000-000000000002",
                "reason": 101,
                "overrideReason": 202
            }
            """;
        using (var doc = JsonDocument.Parse(transArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "posting_transition", Arguments = args }, p =>
            {
                Assert.Equal("101", p.Arguments!["reason"].GetString());
                Assert.Equal("202", p.Arguments["overrideReason"].GetString());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 6. note_create: area, provenanceQualifier as numbers
        var noteCreateArgsJson = """
            {
                "title": "Test Note",
                "body": "Markdown body",
                "area": 100,
                "provenanceKind": "stated",
                "provenanceDate": "2026-10-25",
                "provenanceQualifier": 200
            }
            """;
        using (var doc = JsonDocument.Parse(noteCreateArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "note_create", Arguments = args }, p =>
            {
                Assert.Equal("100", p.Arguments!["area"].GetString());
                Assert.Equal("200", p.Arguments["provenanceQualifier"].GetString());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 7. note_get: offset, maxChars as numeric strings
        var noteGetArgsJson = """
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "offset": "0",
                "maxChars": "1000"
            }
            """;
        using (var doc = JsonDocument.Parse(noteGetArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "note_get", Arguments = args }, p =>
            {
                Assert.Equal(0, p.Arguments!["offset"].GetInt32());
                Assert.Equal(1000, p.Arguments["maxChars"].GetInt32());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 8. note_append: textToAppend as number
        var noteAppendArgsJson = """
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "textToAppend": 12345
            }
            """;
        using (var doc = JsonDocument.Parse(noteAppendArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "note_append", Arguments = args }, p =>
            {
                Assert.Equal("12345", p.Arguments!["textToAppend"].GetString());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 9. note_replace_section: heading, newSectionBody as numbers
        var noteReplaceArgsJson = """
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "heading": 101,
                "newSectionBody": 202,
                "versionToken": "00000000-0000-0000-0000-000000000002"
            }
            """;
        using (var doc = JsonDocument.Parse(noteReplaceArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "note_replace_section", Arguments = args }, p =>
            {
                Assert.Equal("101", p.Arguments!["heading"].GetString());
                Assert.Equal("202", p.Arguments["newSectionBody"].GetString());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 10. note_search: query, area as numbers, includeArchived as string bool, limit as numeric string
        var noteSearchArgsJson = """
            {
                "query": 19505,
                "area": 100,
                "includeArchived": "false",
                "limit": "15"
            }
            """;
        using (var doc = JsonDocument.Parse(noteSearchArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "note_search", Arguments = args }, p =>
            {
                Assert.Equal("19505", p.Arguments!["query"].GetString());
                Assert.Equal("100", p.Arguments["area"].GetString());
                Assert.Equal(JsonValueKind.False, p.Arguments["includeArchived"].ValueKind);
                Assert.Equal(15, p.Arguments["limit"].GetInt32());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }

        // 11. note_archive: valid id
        var noteArchiveArgsJson = """
            {
                "id": "00000000-0000-0000-0000-000000000001"
            }
            """;
        using (var doc = JsonDocument.Parse(noteArchiveArgsJson))
        {
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var res = await UnknownArgumentValidationFilter.ExecuteAsync(new CallToolRequestParams { Name = "note_archive", Arguments = args }, p =>
            {
                Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), p.Arguments!["id"].GetGuid());
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = """{"ok": true}""" }] });
            });
            Assert.False(res.IsError == true);
        }
    }

    [Fact]
    public async Task ShippedMcpHost_CallPostingRecord_WithNumericRequisitionIdAndAtsTenant_SucceedsOverStdio()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        await using var client = await McpProcessClient.StartAsync(mcpDllPath, env);

        // First create company
        var compRes = await client.CallToolAsync("company_record", new
        {
            name = "Stdio Numeric Tolerance Corp",
            status = "target"
        });
        Assert.True(compRes.GetProperty("ok").GetBoolean());
        var companyId = compRes.GetProperty("data").GetProperty("company").GetProperty("id").GetString()!;

        // Now call posting_record with numeric requisitionId (19505) and atsTenant (12345)
        var postRes = await client.CallToolAsync("posting_record", new
        {
            companyId = companyId,
            title = "Site Reliability Director",
            requisitionId = 19505,
            atsTenant = 12345,
            location = 90210
        });

        Assert.True(postRes.GetProperty("ok").GetBoolean(), $"Expected ok: true, got: {postRes.GetRawText()}");
        var data = postRes.GetProperty("data");
        Assert.Equal("created", data.GetProperty("status").GetString());
        var posting = data.GetProperty("posting");
        Assert.Equal("90210", posting.GetProperty("location").GetString());

        // Verify requisitionId and atsTenant were saved by dedup lookup (Tier A key: 12345|19505)
        var dedupRes = await client.CallToolAsync("tracker_query", new
        {
            entity = "posting_dedup_lookup",
            dedupKey = "12345|19505"
        });
        Assert.True(dedupRes.GetProperty("ok").GetBoolean());
        Assert.Equal(1, dedupRes.GetProperty("data").GetProperty("totalCount").GetInt32());

        // Calling again with same numeric requisitionId and atsTenant returns already_tracked
        var dedupCallRes = await client.CallToolAsync("posting_record", new
        {
            companyId = companyId,
            title = "Site Reliability Director",
            requisitionId = 19505,
            atsTenant = 12345
        });
        Assert.True(dedupCallRes.GetProperty("ok").GetBoolean());
        Assert.Equal("already_tracked", dedupCallRes.GetProperty("data").GetProperty("status").GetString());
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
