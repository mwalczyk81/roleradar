using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Queries;

public sealed record GetContactQuery(Guid Id, string? OwnerId = null) : IQuery<ContactDto?>;

public sealed record ContactDto(
    Guid Id,
    string Name,
    string? Role,
    string? CompanyAffiliation,
    Guid? CompanyId,
    string Relationship,
    string? Notes,
    bool IsRedacted,
    Guid VersionToken,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class GetContactQueryHandler : IQueryHandler<GetContactQuery, ContactDto?>
{
    private readonly ITrackingTransactionContext _context;

    public GetContactQueryHandler(ITrackingTransactionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Result<ContactDto?>> HandleAsync(GetContactQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Id == Guid.Empty)
        {
            return Result<ContactDto?>.Failure(ErrorCode.ValidationError, "Contact ID cannot be empty.");
        }

        var ownerId = string.IsNullOrWhiteSpace(query.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(query.OwnerId).Value;

        var result = await _context.Contacts.GetByIdAsync(ownerId, query.Id, cancellationToken);
        if (!result.IsSuccess)
        {
            return Result<ContactDto?>.Failure(result.Error!.Value);
        }

        if (result.Value is null)
        {
            return Result<ContactDto?>.Failure(ErrorCode.NotFound, $"Contact '{query.Id}' not found.");
        }

        var contact = result.Value;
        return Result<ContactDto?>.Success(new ContactDto(
            contact.Id,
            contact.Name,
            contact.Role,
            contact.CompanyAffiliation,
            contact.CompanyId,
            contact.Relationship.ToSnakeCase(),
            contact.Notes,
            contact.IsRedacted,
            contact.VersionToken,
            contact.CreatedAt,
            contact.UpdatedAt));
    }
}
