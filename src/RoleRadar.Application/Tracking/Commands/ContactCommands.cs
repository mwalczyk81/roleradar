using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Commands;

public sealed record AddContactCommand(
    string Name,
    string? Role = null,
    string? Company = null,
    Guid? CompanyId = null,
    string? Relationship = null,
    string? Notes = null,
    string? Actor = null,
    string? OwnerId = null,
    Guid? Id = null) : ICommand<ContactResultDto>;

public sealed record RedactContactCommand(
    Guid Id,
    Guid? VersionToken = null,
    string? Actor = null,
    string? OwnerId = null) : ICommand<ContactRedactResultDto>;

public sealed record ContactResultDto(
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

public sealed record ContactRedactResultDto(
    Guid Id,
    Guid VersionToken,
    string Status);

public sealed class AddContactCommandHandler : ICommandHandler<AddContactCommand, ContactResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public AddContactCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<ContactResultDto>> HandleAsync(AddContactCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result<ContactResultDto>.Failure(ErrorCode.ValidationError, "Contact name is required.");
        }

        var trimmedName = command.Name.Trim();
        if (trimmedName.Length > Contact.MaxNameLength)
        {
            return Result<ContactResultDto>.Failure(ErrorCode.ValidationError, $"Contact name cannot exceed {Contact.MaxNameLength} characters.");
        }

        if (command.Role is not null && command.Role.Trim().Length > Contact.MaxRoleLength)
        {
            return Result<ContactResultDto>.Failure(ErrorCode.ValidationError, $"Contact role cannot exceed {Contact.MaxRoleLength} characters.");
        }

        if (command.Company is not null && command.Company.Trim().Length > Contact.MaxCompanyAffiliationLength)
        {
            return Result<ContactResultDto>.Failure(ErrorCode.ValidationError, $"Company affiliation cannot exceed {Contact.MaxCompanyAffiliationLength} characters.");
        }

        if (command.Notes is not null && command.Notes.Length > Contact.MaxNotesLength)
        {
            return Result<ContactResultDto>.Failure(ErrorCode.ValidationError, $"Contact notes cannot exceed {Contact.MaxNotesLength} characters.");
        }

        var relationship = ContactRelationship.Peer;
        if (!string.IsNullOrWhiteSpace(command.Relationship))
        {
            var relStr = command.Relationship.Trim().ToLowerInvariant();
            relationship = relStr switch
            {
                "recruiter" => ContactRelationship.Recruiter,
                "referral" => ContactRelationship.Referral,
                "hiring_manager" or "hiringmanager" => ContactRelationship.HiringManager,
                "peer" => ContactRelationship.Peer,
                _ => ContactRelationship.Peer
            };
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;

        var now = _clock.UtcNow;

        var contact = Contact.Create(
            ownerId: ownerId,
            name: trimmedName,
            role: command.Role,
            companyAffiliation: command.Company,
            companyId: command.CompanyId,
            relationship: relationship,
            notes: command.Notes,
            id: command.Id,
            now: now);

        await _context.Contacts.UpsertContactAsync(contact, cancellationToken);

        // Audit entry: operation "contact_add", changed fields only, zero names/notes
        var changedFields = new List<string> { "name" };
        if (contact.Role is not null) changedFields.Add("role");
        if (contact.CompanyAffiliation is not null) changedFields.Add("company_affiliation");
        if (contact.CompanyId.HasValue) changedFields.Add("company_id");
        changedFields.Add("relationship");
        if (contact.Notes is not null) changedFields.Add("notes");

        var auditSummary = new ContactAuditContentSummary(
            ContactId: contact.Id,
            ChangedFieldNames: changedFields,
            IsRedacted: false,
            Timestamp: now);

        var auditEntry = AuditLogEntry.Create(
            ownerId: ownerId,
            actor: command.Actor ?? "cli-user",
            operation: "contact_add",
            targetNoteId: contact.Id,
            noteRevisionId: null,
            appendedText: string.Join(",", changedFields),
            resultingVersionToken: contact.VersionToken,
            resolvedDefaultDate: null,
            occurredAtUtc: now,
            beforeSummary: null,
            afterSummary: new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: changedFields.Count,
                status: "contact_add",
                excerpt: string.Empty));

        await _context.AppendAuditEntryAsync(auditEntry, cancellationToken);

        return Result<ContactResultDto>.Success(new ContactResultDto(
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

public sealed class RedactContactCommandHandler : ICommandHandler<RedactContactCommand, ContactRedactResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public RedactContactCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<ContactRedactResultDto>> HandleAsync(RedactContactCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Id == Guid.Empty)
        {
            return Result<ContactRedactResultDto>.Failure(ErrorCode.ValidationError, "Contact ID cannot be empty.");
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;

        var existingResult = await _context.Contacts.GetByIdAsync(ownerId, command.Id, cancellationToken);
        if (!existingResult.IsSuccess || existingResult.Value is null)
        {
            return Result<ContactRedactResultDto>.Failure(ErrorCode.NotFound, $"Contact '{command.Id}' not found.");
        }

        var contact = existingResult.Value;

        // Idempotency: if already redacted, write nothing and return already_redacted
        if (contact.IsRedacted)
        {
            return Result<ContactRedactResultDto>.Success(new ContactRedactResultDto(
                contact.Id,
                contact.VersionToken,
                "already_redacted"));
        }

        // Concurrency token check
        if (command.VersionToken.HasValue && command.VersionToken.Value != contact.VersionToken)
        {
            return Result<ContactRedactResultDto>.Failure(ErrorCode.StaleVersionToken, "Contact version token mismatch or stale.");
        }

        var now = _clock.UtcNow;
        contact.Redact(now);

        await _context.Contacts.UpsertContactAsync(contact, cancellationToken);

        // Audit entry: operation "contact_redact", changed fields only, zero names/notes
        var changedFields = new[] { "name", "role", "company_affiliation", "company_id", "relationship", "notes", "is_redacted" };
        var auditEntry = AuditLogEntry.Create(
            ownerId: ownerId,
            actor: command.Actor ?? "cli-user",
            operation: "contact_redact",
            targetNoteId: contact.Id,
            noteRevisionId: null,
            appendedText: string.Join(",", changedFields),
            resultingVersionToken: contact.VersionToken,
            resolvedDefaultDate: null,
            occurredAtUtc: now,
            beforeSummary: new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: 0,
                status: "prior",
                excerpt: string.Empty),
            afterSummary: new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: changedFields.Length,
                status: "contact_redact",
                excerpt: string.Empty));

        await _context.AppendAuditEntryAsync(auditEntry, cancellationToken);

        return Result<ContactRedactResultDto>.Success(new ContactRedactResultDto(
            contact.Id,
            contact.VersionToken,
            "redacted"));
    }
}
