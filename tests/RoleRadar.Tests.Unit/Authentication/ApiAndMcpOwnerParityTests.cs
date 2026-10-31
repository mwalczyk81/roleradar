using System.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RoleRadar.Api.Authentication;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using Xunit;

namespace RoleRadar.Tests.Unit.Authentication;

public sealed class ApiAndMcpOwnerParityTests
{
    private const string AllowedSub = "0192e4a1-0000-7000-8000-000000000001";
    private static readonly DateTime FixedNow = new(2026, 10, 25, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DataCreatedByMcpCliOwner_IsReadableThroughApiHandlerPath_UnderSameOwner()
    {
        // 1. Arrange: MCP / CLI writes data under OwnerId.DefaultValue ("local-owner")
        var repo = new ParityFakeNoteRepository();
        var mcpOwnerId = OwnerId.Default.Value;

        var note = Note.Create(
            ownerId: mcpOwnerId,
            title: "Staff Platform Engineer Criteria",
            body: "# Staff Criteria\nRemote .NET roles only.",
            tags: ["remote", "dotnet"],
            area: "criteria",
            provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-25", "mcp-import"),
            createdAtUtc: FixedNow);

        await repo.InsertAsync(note);

        // 2. Arrange API pipeline: Authenticated HTTP context with allowlisted sub claim
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [new Claim("sub", AllowedSub)],
            authenticationType: "Bearer");
        httpContext.User = new ClaimsPrincipal(identity);

        var contextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub]
        });

        var ownerIdProvider = new HttpUserOwnerIdProvider(contextAccessor, options);

        // 3. Act: API endpoint resolves owner via IOwnerIdProvider and invokes application query handler
        var resolvedOwnerId = ownerIdProvider.GetOwnerId().Value;
        var queryHandler = new GetNoteQueryHandler(repo);
        var result = await queryHandler.HandleAsync(new GetNoteQuery(note.Id, OwnerId: resolvedOwnerId));

        // 4. Assert: Data imported/created by MCP/CLI is visible through the API
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(note.Id, result.Value.Id);
        Assert.Equal("Staff Platform Engineer Criteria", result.Value.Title);
        Assert.Equal(mcpOwnerId, resolvedOwnerId);
    }

    [Fact]
    public void UnauthenticatedApiRequest_ThrowsSecurityException_WithNoFallbackToDefaultOwner()
    {
        // Arrange: Unauthenticated HTTP context
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var contextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub]
        });

        var ownerIdProvider = new HttpUserOwnerIdProvider(contextAccessor, options);

        // Act & Assert: Must throw SecurityException immediately; zero fallback to default owner
        Assert.Throws<SecurityException>(() => ownerIdProvider.GetOwnerId());
    }

    private sealed class ParityFakeNoteRepository : INoteRepository
    {
        private readonly Dictionary<Guid, Note> _notes = [];

        public Task InsertAsync(Note note, CancellationToken cancellationToken = default)
        {
            _notes[note.Id] = note;
            return Task.CompletedTask;
        }

        public Task<Result<Note>> GetByIdAsync(string ownerId, Guid noteId, CancellationToken cancellationToken = default)
        {
            if (_notes.TryGetValue(noteId, out var note) && note.OwnerId == ownerId)
            {
                return Task.FromResult(Result<Note>.Success(note));
            }

            return Task.FromResult(Result<Note>.Failure(ErrorCode.NotFound, "Note not found."));
        }

        public Task<Result<StoreReadinessStatus>> CheckReadinessAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<StoreReadinessStatus>.Success(new StoreReadinessStatus(true, "localhost", "rs0", 1.0)));

        public Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
            Func<INoteTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<IReadOnlyList<NoteSearchHit>>> SearchAsync(NoteSearchCriteria criteria, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<Note>> UpdateAsync(Note note, Guid originalVersionToken, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<IReadOnlyList<Note>>> GetImportedBySourceFileAsync(string ownerId, string sourceFileId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<IReadOnlyList<NoteRevision>>> GetRevisionsAsync(string ownerId, Guid noteId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<IReadOnlyList<AuditLogEntry>>> GetAuditEntriesAsync(string ownerId, Guid? noteId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
