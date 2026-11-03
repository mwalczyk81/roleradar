namespace RoleRadar.Application.Reading;

using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;

public interface IOwnerReadSeam
{
    IQueryable<PostingReadModel> QueryPostings(OwnerId ownerId);
    IQueryable<CompanyReadModel> QueryCompanies(OwnerId ownerId);
    IQueryable<ApplicationReadModel> QueryApplications(OwnerId ownerId);

    /// <summary>
    /// Notes are queried without their (up to 8 MiB) body unless <paramref name="includeContent"/> is true;
    /// the exclusion happens in the database query, so bodies are never read from MongoDB for list pages.
    /// </summary>
    IQueryable<NoteReadModel> QueryNotes(OwnerId ownerId, bool includeContent = false);

    IQueryable<CriterionReadModel> QueryCriteria(OwnerId ownerId);
    IQueryable<SearchRunReadModel> QuerySearchRuns(OwnerId ownerId);
}
