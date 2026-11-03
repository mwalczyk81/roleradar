namespace RoleRadar.Application.Reading.Models;

public sealed class SearchRunReadModel
{
    public Guid Id { get; init; }
    public DateTimeOffset RunDate { get; init; }
    public int NewPostings { get; init; }

    // The search-runs collection stores neither a companies-searched nor an errors counter, and every
    // stored run counts only its new postings. These three mirror the REST SearchRunDto mapping
    // (0, new postings, 0) so both read paths agree; they are not stored, filterable or sortable.
    public int CompaniesSearched
    {
        get => 0;
        init { }
    }

    public int PostingsFound
    {
        get => NewPostings;
        init { }
    }

    public int ErrorsCount
    {
        get => 0;
        init { }
    }
    public string? Summary { get; init; }

    internal string OwnerId { get; init; } = string.Empty;
}
