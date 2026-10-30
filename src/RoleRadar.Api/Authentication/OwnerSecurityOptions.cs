namespace RoleRadar.Api.Authentication;

public sealed class OwnerSecurityOptions
{
    public const string SectionName = "Owner";

    public string[] AllowedSubjects { get; set; } = [];
}
