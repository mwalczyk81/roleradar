namespace RoleRadar.Application.Reading;

using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;

public interface IOwnerReadSeam
{
    IQueryable<PostingReadModel> QueryPostings(OwnerId ownerId);
    IQueryable<CompanyReadModel> QueryCompanies(OwnerId ownerId);
    IQueryable<ApplicationReadModel> QueryApplications(OwnerId ownerId);
    IQueryable<NoteReadModel> QueryNotes(OwnerId ownerId);
    IQueryable<CriterionReadModel> QueryCriteria(OwnerId ownerId);
    IQueryable<SearchRunReadModel> QuerySearchRuns(OwnerId ownerId);
}
