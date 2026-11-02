namespace RoleRadar.Application.Reading.Models;

public sealed class SearchRunReadModel
{
    public Guid Id { get; init; }
    public DateTimeOffset RunDate { get; init; }
    public int CompaniesSearched { get; init; }
    public int PostingsFound { get; init; }
    public int NewPostings { get; init; }
    public int ErrorsCount { get; init; }
    public string? Summary { get; init; }

    internal string OwnerId { get; init; } = string.Empty;
}
