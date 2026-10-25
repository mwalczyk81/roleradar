namespace RoleRadar.Api.Authentication;

public sealed class OwnerSecurityOptions
{
    public const string SectionName = "OwnerSecurity";

    public string[] AllowedSubjects { get; set; } = [];
}
