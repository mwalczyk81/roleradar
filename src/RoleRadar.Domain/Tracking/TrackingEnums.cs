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

public static class TrackingEnumExtensions
{
    public static string ToSnakeCase(this PostingDisposition disposition) => disposition switch
    {
        PostingDisposition.Lead => "lead",
        PostingDisposition.Unscored => "unscored",
        PostingDisposition.Scored => "scored",
        PostingDisposition.Applied => "applied",
        PostingDisposition.ScreenedOut => "screened_out",
        PostingDisposition.Closed => "closed",
        PostingDisposition.Rejected => "rejected",
        _ => disposition.ToString().ToLowerInvariant()
    };

    public static bool TryParseDisposition(string? val, out PostingDisposition disposition)
    {
        disposition = PostingDisposition.Lead;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "lead": disposition = PostingDisposition.Lead; return true;
            case "unscored": disposition = PostingDisposition.Unscored; return true;
            case "scored": disposition = PostingDisposition.Scored; return true;
            case "applied": disposition = PostingDisposition.Applied; return true;
            case "screened_out":
            case "screenedout": disposition = PostingDisposition.ScreenedOut; return true;
            case "closed": disposition = PostingDisposition.Closed; return true;
            case "rejected": disposition = PostingDisposition.Rejected; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this ResurfacePolicy policy) => policy switch
    {
        ResurfacePolicy.Never => "never",
        ResurfacePolicy.IfReopened => "if_reopened",
        _ => policy.ToString().ToLowerInvariant()
    };

    public static bool TryParseResurfacePolicy(string? val, out ResurfacePolicy policy)
    {
        policy = ResurfacePolicy.Never;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "never": policy = ResurfacePolicy.Never; return true;
            case "if_reopened":
            case "ifreopened": policy = ResurfacePolicy.IfReopened; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this SourceKind kind) => kind switch
    {
        SourceKind.Ats => "ats",
        SourceKind.Aggregator => "aggregator",
        _ => kind.ToString().ToLowerInvariant()
    };

    public static bool TryParseSourceKind(string? val, out SourceKind kind)
    {
        kind = SourceKind.Ats;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "ats": kind = SourceKind.Ats; return true;
            case "aggregator": kind = SourceKind.Aggregator; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this RemoteType remoteType) => remoteType switch
    {
        RemoteType.Remote => "remote",
        RemoteType.Hybrid => "hybrid",
        RemoteType.Onsite => "onsite",
        _ => "unknown"
    };

    public static bool TryParseRemoteType(string? val, out RemoteType remoteType)
    {
        remoteType = RemoteType.Unknown;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "remote": remoteType = RemoteType.Remote; return true;
            case "hybrid": remoteType = RemoteType.Hybrid; return true;
            case "onsite": remoteType = RemoteType.Onsite; return true;
            case "unknown": remoteType = RemoteType.Unknown; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this CultureLevel cultureLevel) => cultureLevel switch
    {
        CultureLevel.Stable => "stable",
        CultureLevel.Moderate => "moderate",
        CultureLevel.HighRisk => "high_risk",
        _ => "unknown"
    };

    public static bool TryParseCultureLevel(string? val, out CultureLevel cultureLevel)
    {
        cultureLevel = CultureLevel.Unknown;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "stable": cultureLevel = CultureLevel.Stable; return true;
            case "moderate": cultureLevel = CultureLevel.Moderate; return true;
            case "high_risk":
            case "highrisk": cultureLevel = CultureLevel.HighRisk; return true;
            case "unknown": cultureLevel = CultureLevel.Unknown; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this SignalType signalType) => signalType switch
    {
        SignalType.Repost => "repost",
        SignalType.NoSalary => "no_salary",
        SignalType.ParallelPosting => "parallel_posting",
        SignalType.Gone => "gone",
        SignalType.Phantom => "phantom",
        SignalType.Deadline => "deadline",
        _ => signalType.ToString().ToLowerInvariant()
    };

    public static bool TryParseSignalType(string? val, out SignalType signalType)
    {
        signalType = SignalType.Repost;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "repost": signalType = SignalType.Repost; return true;
            case "no_salary":
            case "nosalary": signalType = SignalType.NoSalary; return true;
            case "parallel_posting":
            case "parallelposting": signalType = SignalType.ParallelPosting; return true;
            case "gone": signalType = SignalType.Gone; return true;
            case "phantom": signalType = SignalType.Phantom; return true;
            case "deadline": signalType = SignalType.Deadline; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this OwnershipType ownershipType) => ownershipType switch
    {
        OwnershipType.Public => "public",
        OwnershipType.Private => "private",
        OwnershipType.PeBacked => "pe_backed",
        OwnershipType.VcBacked => "vc_backed",
        OwnershipType.Mutual => "mutual",
        OwnershipType.Subsidiary => "subsidiary",
        OwnershipType.Nonprofit => "nonprofit",
        OwnershipType.Government => "government",
        _ => "unknown"
    };

    public static bool TryParseOwnershipType(string? val, out OwnershipType type)
    {
        type = OwnershipType.Unknown;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "public": type = OwnershipType.Public; return true;
            case "private": type = OwnershipType.Private; return true;
            case "pe_backed":
            case "pebacked": type = OwnershipType.PeBacked; return true;
            case "vc_backed":
            case "vcbacked": type = OwnershipType.VcBacked; return true;
            case "mutual": type = OwnershipType.Mutual; return true;
            case "subsidiary": type = OwnershipType.Subsidiary; return true;
            case "nonprofit": type = OwnershipType.Nonprofit; return true;
            case "government": type = OwnershipType.Government; return true;
            case "unknown": type = OwnershipType.Unknown; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this CompanyStatus status) => status switch
    {
        CompanyStatus.Target => "target",
        CompanyStatus.Watch => "watch",
        CompanyStatus.Avoid => "avoid",
        CompanyStatus.Neutral => "neutral",
        _ => status.ToString().ToLowerInvariant()
    };

    public static bool TryParseCompanyStatus(string? val, out CompanyStatus status)
    {
        status = CompanyStatus.Neutral;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "target": status = CompanyStatus.Target; return true;
            case "watch": status = CompanyStatus.Watch; return true;
            case "avoid": status = CompanyStatus.Avoid; return true;
            case "neutral": status = CompanyStatus.Neutral; return true;
            default: return false;
        }
    }

    public static string ToSnakeCase(this FetchMethod method) => method switch
    {
        FetchMethod.Api => "api",
        FetchMethod.Browser => "browser",
        FetchMethod.Blocked => "blocked",
        _ => method.ToString().ToLowerInvariant()
    };

    public static bool TryParseFetchMethod(string? val, out FetchMethod method)
    {
        method = FetchMethod.Api;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "api": method = FetchMethod.Api; return true;
            case "browser": method = FetchMethod.Browser; return true;
            case "blocked": method = FetchMethod.Blocked; return true;
            default: return false;
        }
    }
}
