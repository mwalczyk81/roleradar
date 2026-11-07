using System.Reflection;
using RoleRadar.Api.Models;
using RoleRadar.Domain.Notes;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Api.OpenApi;

/// <summary>A DTO property (CLR name) that exposes a Domain enum as a plain string.</summary>
public sealed record EnumDtoProperty(Type Dto, string ClrProperty);

/// <summary>One Domain enum-typed property and the DTO properties that carry its serialized values.</summary>
public sealed record EnumContractEntry(Type DomainOwner, string DomainProperty, Type EnumType, IReadOnlyList<EnumDtoProperty> CarriedBy);

/// <summary>A Domain enum-typed property deliberately not constrained in the API schema, with the reason.</summary>
public sealed record EnumAllowlistEntry(Type DomainOwner, string DomainProperty, string Reason);

/// <summary>
/// The single mapping from Domain enum properties to the API properties that carry them. The OpenAPI schema
/// transformer writes <c>enum</c> from it, so contracts/openapi/roleradar-api.json and the generated client types
/// cannot drift from Domain (ADR 0035). EnumContractRegistryTests fails when a Domain enum property is in neither
/// list. DTOs bind these values as plain strings on purpose (handlers answer a bad value with the ErrorEnvelope), so
/// only the schema, not the binding, carries the constraint. OData stays Edm.String (ADR 0028).
/// </summary>
public static class EnumContractRegistry
{
    private const string NotInRestApi = "Not exposed by the REST API schema; reaches clients only through MCP tools and OData, where the string and tolower() rules apply (ADR 0028).";

    public static IReadOnlyList<EnumContractEntry> Entries { get; } =
    [
        Entry<Posting, PostingDisposition>(nameof(Posting.Disposition),
            (typeof(PostingDetailDto), nameof(PostingDetailDto.Disposition)),
            (typeof(TransitionPostingRequestDto), nameof(TransitionPostingRequestDto.ToDisposition))),
        Entry<Posting, ResurfacePolicy>(nameof(Posting.ResurfacePolicy),
            (typeof(PostingDetailDto), nameof(PostingDetailDto.ResurfacePolicy)),
            (typeof(TransitionPostingRequestDto), nameof(TransitionPostingRequestDto.ResurfacePolicy))),
        Entry<PostingSignal, SignalType>(nameof(PostingSignal.SignalType),
            (typeof(PostingSignalDto), nameof(PostingSignalDto.SignalType))),
        Entry<Company, CompanyStatus>(nameof(Company.Status),
            (typeof(CompanyDetailDto), nameof(CompanyDetailDto.Status)),
            (typeof(UpdateCompanyRequestDto), nameof(UpdateCompanyRequestDto.Status))),
        Entry<Company, OwnershipType>(nameof(Company.OwnershipType),
            (typeof(CompanyDetailDto), nameof(CompanyDetailDto.OwnershipType)),
            (typeof(UpdateCompanyRequestDto), nameof(UpdateCompanyRequestDto.OwnershipType))),
        Entry<CareerSource, FetchMethod>(nameof(CareerSource.FetchMethod),
            (typeof(CareerSourceDto), nameof(CareerSourceDto.FetchMethod)),
            (typeof(UpdateCareerSourceRequestDto), nameof(UpdateCareerSourceRequestDto.FetchMethod))),
        // "Application status" in the API is the Domain enum ApplicationOutcome.
        Entry<Domain.Tracking.Application, ApplicationOutcome>(nameof(Domain.Tracking.Application.Outcome),
            (typeof(ApplicationDetailDto), nameof(ApplicationDetailDto.Status))),
        Entry<Domain.Tracking.Application, ResumeVariant>(nameof(Domain.Tracking.Application.ResumeVariant),
            (typeof(RecordApplicationRequestDto), nameof(RecordApplicationRequestDto.ResumeVariant))),
        Entry<ApplicationEvent, ApplicationEventType>(nameof(ApplicationEvent.EventType),
            (typeof(ApplicationEventDto), nameof(ApplicationEventDto.EventType)),
            (typeof(AppendApplicationEventRequestDto), nameof(AppendApplicationEventRequestDto.EventType)))
    ];

    public static IReadOnlyList<EnumAllowlistEntry> Allowlist { get; } =
    [
        Allow<Posting>(nameof(Posting.SourceKind), NotInRestApi),
        Allow<Posting>(nameof(Posting.RemoteType), NotInRestApi),
        Allow<Posting>(nameof(Posting.CultureLevel), NotInRestApi),
        Allow<PostingCandidate>(nameof(PostingCandidate.Disposition), "Import-pipeline candidate; never serialized by the API."),
        Allow<Contact>(nameof(Contact.Relationship), NotInRestApi),
        Allow<Criterion>(nameof(Criterion.Category), NotInRestApi),
        Allow<Criterion>(nameof(Criterion.Provenance), NotInRestApi),
        Allow<ResolvedDateOutcome>(nameof(ResolvedDateOutcome.Kind), "Internal date-inference result; never serialized by the API."),
        Allow<Note>(nameof(Note.Status), "Note lifecycle value; NoteDto does not expose it as a typed field."),
        Allow<ProvenanceMarker>(nameof(ProvenanceMarker.Kind), "Parsed from note Markdown; not an API field."),
        Allow<SectionReplacementOutcome>(nameof(SectionReplacementOutcome.Status), "Internal section-replace result; not an API field.")
    ];

    /// <summary>The serialized (snake_case) values of a registered enum, in declaration order.</summary>
    public static IReadOnlyList<string> SerializedValues(Type enumType)
    {
        var method = typeof(Company).Assembly.GetExportedTypes()
            .Where(t => t.IsAbstract && t.IsSealed)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(m => m.Name == "ToSnakeCase" && m.GetParameters() is [var p] && p.ParameterType == enumType);

        return [.. Enum.GetValues(enumType).Cast<object>().Select(v => (string)method.Invoke(null, [v])!)];
    }

    private static EnumContractEntry Entry<TOwner, TEnum>(string property, params (Type Dto, string ClrProperty)[] carriedBy)
        where TEnum : struct, Enum =>
        new(typeof(TOwner), property, typeof(TEnum), [.. carriedBy.Select(c => new EnumDtoProperty(c.Dto, c.ClrProperty))]);

    private static EnumAllowlistEntry Allow<TOwner>(string property, string reason) => new(typeof(TOwner), property, reason);
}
