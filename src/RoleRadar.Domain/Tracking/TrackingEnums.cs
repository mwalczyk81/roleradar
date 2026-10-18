namespace RoleRadar.Domain.Tracking;

public enum OwnershipType
{
    Public,
    Private,
    PeBacked,
    VcBacked,
    Mutual,
    Subsidiary,
    Nonprofit,
    Government,
    Unknown
}

public enum CompanyStatus
{
    Target,
    Watch,
    Avoid,
    Neutral
}

public enum CultureLevel
{
    Stable,
    Moderate,
    HighRisk,
    Unknown
}

public enum PostingDisposition
{
    Lead,
    Scored,
    Applied,
    Rejected,
    ScreenedOut,
    Closed,
    Unscored
}

public enum ResurfacePolicy
{
    Never,
    IfReopened
}

public enum SourceKind
{
    Ats,
    Aggregator
}

public enum RemoteType
{
    Remote,
    Hybrid,
    Onsite,
    Unknown
}

public enum FetchMethod
{
    Api,
    Browser,
    Blocked
}

public enum CriterionCategory
{
    HardSkip,
    Weight,
    Preference
}

public enum ProvenanceKind
{
    Stated,
    Inferred
}

public enum ResumeVariant
{
    Tailored,
    AsIs,
    IcFramed
}

public enum ApplicationOutcome
{
    Applied,
    Rejected,
    Closed
}

public enum ApplicationEventType
{
    Applied,
    FollowUp,
    Interview,
    Offer,
    OfferAccepted,
    OfferDeclined,
    Rejected,
    Withdrawn
}

public enum SignalType
{
    Repost,
    NoSalary,
    ParallelPosting,
    Gone,
    Phantom,
    Deadline
}

public enum ContactRelationship
{
    Recruiter,
    Referral,
    HiringManager,
    Peer,
    Redacted
}
