using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Application.Notes.Queries;

namespace RoleRadar.Mcp.Stdio.Tools;

[McpServerToolType]
public sealed class NoteMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ICommandHandler<CreateNoteCommand, NoteCreatedDto> _createNoteHandler;
    private readonly IQueryHandler<GetNoteQuery, NoteDetailDto> _getNoteHandler;
    private readonly ICommandHandler<AppendToNoteCommand, NoteUpdatedDto> _appendToNoteHandler;
    private readonly ICommandHandler<ReplaceNoteSectionCommand, NoteSectionReplacedDto> _replaceNoteSectionHandler;
    private readonly IQueryHandler<SearchNotesQuery, NoteSearchResponseDto> _searchNotesHandler;

    public NoteMcpTools(
        ICommandHandler<CreateNoteCommand, NoteCreatedDto> createNoteHandler,
        IQueryHandler<GetNoteQuery, NoteDetailDto> getNoteHandler,
        ICommandHandler<AppendToNoteCommand, NoteUpdatedDto> appendToNoteHandler,
        ICommandHandler<ReplaceNoteSectionCommand, NoteSectionReplacedDto> replaceNoteSectionHandler,
        IQueryHandler<SearchNotesQuery, NoteSearchResponseDto> searchNotesHandler)
    {
        ArgumentNullException.ThrowIfNull(createNoteHandler);
        ArgumentNullException.ThrowIfNull(getNoteHandler);
        ArgumentNullException.ThrowIfNull(appendToNoteHandler);
        ArgumentNullException.ThrowIfNull(replaceNoteSectionHandler);
        ArgumentNullException.ThrowIfNull(searchNotesHandler);

        _createNoteHandler = createNoteHandler;
        _getNoteHandler = getNoteHandler;
        _appendToNoteHandler = appendToNoteHandler;
        _replaceNoteSectionHandler = replaceNoteSectionHandler;
        _searchNotesHandler = searchNotesHandler;
    }

    [McpServerTool(Name = "note_create"), Description("Creates a durable markdown note with title, body, tags, area label, and provenance marker (stated, researched, or inferred with YYYY-MM-DD date).")]
    public async Task<string> NoteCreateAsync(
        [Description("Non-empty title of the note.")] string title,
        [Description("Markdown body (supports >100 KB up to 1 MB).")] string body,
        [Description("Area label (e.g., 'criteria', 'runbook', 'preferences', 'general').")] string area,
        [Description("Optional list of tag labels.")] string[]? tags,
        [Description("One of 'stated', 'researched', or 'inferred'.")] string provenanceKind,
        [Description("ISO date 'YYYY-MM-DD'.")] string provenanceDate,
        [Description("Optional provenance qualifier text.")] string? provenanceQualifier = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateNoteCommand(
            Title: title,
            Body: body,
            Area: area,
            Tags: tags,
            ProvenanceKind: provenanceKind,
            ProvenanceDate: provenanceDate,
            ProvenanceQualifier: provenanceQualifier,
            Actor: "mcp-stdio");

        var result = await _createNoteHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "note_get"), Description("Reads a single note in full by its unique id, including archived notes, returning its markdown body, metadata, and current version token.")]
    public async Task<string> NoteGetAsync(
        [Description("Unique identifier of the note.")] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _getNoteHandler.HandleAsync(new GetNoteQuery(id), cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "note_append"), Description("Atomically appends markdown text to the end of an existing note inside a snapshot transaction without requiring a version token.")]
    public async Task<string> NoteAppendAsync(
        [Description("Unique identifier of the note.")] Guid id,
        [Description("Markdown text to append to the existing body.")] string textToAppend,
        CancellationToken cancellationToken = default)
    {
        var command = new AppendToNoteCommand(
            Id: id,
            TextToAppend: textToAppend,
            Actor: "mcp-stdio");

        var result = await _appendToNoteHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "note_replace_section"), Description("Replaces the markdown content under a specific heading in an existing note. Requires the current versionToken and saves the full prior body in revision history.")]
    public async Task<string> NoteReplaceSectionAsync(
        [Description("Unique identifier of the note.")] Guid id,
        [Description("Heading title or heading path to replace (e.g., '## Compensation' or 'Compensation').")] string heading,
        [Description("Replacement markdown content for that section.")] string newSectionBody,
        [Description("Current concurrency versionToken of the note.")] Guid versionToken,
        CancellationToken cancellationToken = default)
    {
        var command = new ReplaceNoteSectionCommand(
            Id: id,
            Heading: heading,
            NewSectionBody: newSectionBody,
            VersionToken: versionToken,
            Actor: "mcp-stdio");

        var result = await _replaceNoteSectionHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "note_search"), Description("Searches notes by case-insensitive text query (supporting stemmed whole words and quoted phrases), tag filter, and area label. Excludes archived notes by default.")]
    public async Task<string> NoteSearchAsync(
        [Description("Free-text search query (supports quoted phrases e.g. '\"staff engineer\"').")] string? query = null,
        [Description("Tag filters combined with logical AND.")] string[]? tags = null,
        [Description("Area label filter combined with logical AND.")] string? area = null,
        [Description("Whether to include archived notes (default false).")] bool includeArchived = false,
        [Description("Maximum results to return (default 20, max 100).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = new SearchNotesQuery(
            Query: query,
            Tags: tags,
            Area: area,
            IncludeArchived: includeArchived,
            Limit: limit);

        var result = await _searchNotesHandler.HandleAsync(searchQuery, cancellationToken);
        return SerializeResult(result);
    }

    private static string SerializeResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return JsonSerializer.Serialize(
                new
                {
                    ok = true,
                    data = result.Value
                },
                JsonOptions);
        }

        var error = result.Error!.Value;
        return JsonSerializer.Serialize(
            new
            {
                ok = false,
                error = new
                {
                    code = error.Code.ToString(),
                    message = error.Message,
                    metadata = error.Metadata
                }
            },
            JsonOptions);
    }
}
