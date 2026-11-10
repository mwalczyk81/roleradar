using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Audit.Queries;
using RoleRadar.Domain.Common;
using Xunit;

namespace RoleRadar.Tests.Unit.Audit;

/// <summary>
/// Feature 013 (FR-020, data-model.md "ListAuditEntriesQuery"): the validation rules, the cursor codec, the shape of a
/// listed item and the offline path of the query handler. Expected failures are results; nothing is thrown.
/// </summary>
public sealed class ListAuditEntriesQueryTests
{
    private static readonly DateTime Moment = new(2026, 11, 9, 14, 3, 22, 418, DateTimeKind.Utc);
    private static readonly string[] AllowedItemMembers =
        ["Actor", "ChangedFields", "EntityId", "EntityType", "Operation", "OccurredAtUtc", "RequestCorrelationId"];

    private sealed class RecordingReader(Result<AuditListPage>? result = null) : IAuditLogReader
    {
        public int Calls { get; private set; }

        public Task<Result<AuditListPage>> ListAsync(ListAuditEntriesQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result ?? Result<AuditListPage>.Success(new AuditListPage([], null)));
        }
    }

    private static async Task<(Result<AuditListPage> Result, RecordingReader Reader)> RunAsync(ListAuditEntriesQuery query, Result<AuditListPage>? readerResult = null)
    {
        var reader = new RecordingReader(readerResult);
        var result = await new ListAuditEntriesQueryHandler(reader).HandleAsync(query);
        return (result, reader);
    }

    private static ListAuditEntriesQuery Query() => new(OwnerId.Default);

    private static async Task AssertRejectedAsync(ListAuditEntriesQuery query, string fieldName)
    {
        var (result, reader) = await RunAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Contains(fieldName, result.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, reader.Calls);
    }

    private static async Task AssertAcceptedAsync(ListAuditEntriesQuery query)
    {
        var (result, reader) = await RunAsync(query);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public void Limit_DefaultsTo25()
    {
        Assert.Equal(25, Query().Limit);
        Assert.Equal(25, ListAuditEntriesQuery.DefaultLimit);
        Assert.Equal(100, ListAuditEntriesQuery.MaxLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task Limit_OutsideOneToOneHundred_IsAValidationError(int limit) =>
        await AssertRejectedAsync(Query() with { Limit = limit }, "limit");

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public async Task Limit_InsideOneToOneHundred_IsAccepted(int limit) =>
        await AssertAcceptedAsync(Query() with { Limit = limit });

    [Theory]
    [InlineData("note")]
    [InlineData("company")]
    [InlineData("posting")]
    [InlineData("application")]
    [InlineData("application_event")]
    [InlineData("contact")]
    [InlineData("criterion")]
    [InlineData("search_run")]
    public async Task EntityType_InTheKnownSet_IsAccepted(string entityType) =>
        await AssertAcceptedAsync(Query() with { EntityType = entityType });

    [Theory]
    [InlineData("widget")]
    [InlineData("")]
    [InlineData("Posting ")]
    [InlineData("posting; drop")]
    public async Task EntityType_OutsideTheKnownSet_IsAValidationError(string entityType) =>
        await AssertRejectedAsync(Query() with { EntityType = entityType }, "entity type");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EntityId_Blank_IsAValidationError(string entityId) =>
        await AssertRejectedAsync(Query() with { EntityId = entityId }, "entity id");

    [Fact]
    public async Task EntityId_Over128Characters_IsAValidationError() =>
        await AssertRejectedAsync(Query() with { EntityId = new string('e', 129) }, "entity id");

    [Fact]
    public async Task EntityId_UpTo128Characters_IsAccepted() =>
        await AssertAcceptedAsync(Query() with { EntityId = new string('e', 128) });

    [Fact]
    public async Task Actor_Empty_IsAValidationError() =>
        await AssertRejectedAsync(Query() with { Actor = string.Empty }, "actor");

    [Fact]
    public async Task Actor_Over256Characters_IsAValidationError() =>
        await AssertRejectedAsync(Query() with { Actor = new string('a', 257) }, "actor");

    [Fact]
    public async Task Actor_UpTo256Characters_IsAccepted() =>
        await AssertAcceptedAsync(Query() with { Actor = new string('a', 256) });

    [Fact]
    public async Task From_AfterTo_IsAValidationError() =>
        await AssertRejectedAsync(
            Query() with { FromUtc = new DateTimeOffset(Moment.AddHours(1)), ToUtc = new DateTimeOffset(Moment) },
            "from");

    [Fact]
    public async Task From_EqualToTo_IsAValidationError() =>
        await AssertRejectedAsync(
            Query() with { FromUtc = new DateTimeOffset(Moment), ToUtc = new DateTimeOffset(Moment) },
            "from");

    [Fact]
    public async Task From_BeforeTo_IsAccepted() =>
        await AssertAcceptedAsync(Query() with { FromUtc = new DateTimeOffset(Moment), ToUtc = new DateTimeOffset(Moment.AddHours(1)) });

    [Fact]
    public async Task OnlyFromOrOnlyTo_IsAccepted()
    {
        await AssertAcceptedAsync(Query() with { FromUtc = new DateTimeOffset(Moment) });
        await AssertAcceptedAsync(Query() with { ToUtc = new DateTimeOffset(Moment) });
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!!")]
    [InlineData("bm90LWEtY3Vyc29y")]
    public async Task Cursor_Malformed_IsAValidationError(string cursor) =>
        await AssertRejectedAsync(Query() with { Cursor = cursor }, "cursor");

    [Fact]
    public void CursorCodec_RoundTripsOccurredAtAndId()
    {
        var id = Guid.CreateVersion7();

        var token = AuditCursorCodec.Encode(Moment, id);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(AuditCursorCodec.TryDecode(token, out var occurredAt, out var decodedId));
        Assert.Equal(Moment, occurredAt);
        Assert.Equal(DateTimeKind.Utc, occurredAt.Kind);
        Assert.Equal(id, decodedId);
    }

    [Fact]
    public void CursorCodec_TokenIsUrlSafe()
    {
        var token = AuditCursorCodec.Encode(Moment, Guid.CreateVersion7());

        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void CursorCodec_RejectsMalformedTokens_WithoutThrowing(string? token) =>
        Assert.False(AuditCursorCodec.TryDecode(token, out _, out _));

    [Fact]
    public async Task ValidCursor_IsAcceptedAndPassedToTheReader()
    {
        var token = AuditCursorCodec.Encode(Moment, Guid.CreateVersion7());

        await AssertAcceptedAsync(Query() with { Cursor = token });
    }

    [Fact]
    public async Task OfflineStore_ReturnsDatastoreOffline_WithoutThrowing()
    {
        var offline = Result<AuditListPage>.Failure(ErrorCode.DatastoreOffline, "The data store is unreachable.");

        var (result, reader) = await RunAsync(Query(), offline);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.DatastoreOffline, result.Error!.Value.Code);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task AnOwnerThatWasNeverSet_IsAValidationError_NotAnException()
    {
        var (result, reader) = await RunAsync(new ListAuditEntriesQuery(default));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void AuditListItem_HasNoMemberThatCouldCarryContent()
    {
        var members = typeof(AuditListItem)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name => name != "EqualityContract")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedItemMembers.Order(StringComparer.Ordinal).ToArray(), members);
    }
}
