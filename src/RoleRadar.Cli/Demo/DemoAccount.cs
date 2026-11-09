namespace RoleRadar.Cli.Demo;

/// <summary>
/// The only committed demo account values, and neither is a secret: a fixed username and a fixed user id. The demo
/// compose file names the same user id as the API's allowed subject, because it must do so before Identity exists.
/// The password and the TOTP key are generated at run time and are never committed or logged (ADR 0039).
/// </summary>
public static class DemoAccount
{
    /// <summary>Fixed, non-secret demo username.</summary>
    public const string Username = "demo-owner";

    /// <summary>
    /// Fixed, non-secret, v7-shaped identifier of the demo user (an Identity user id, not an owner id or a document id).
    /// The one owner-accepted deviation from "ids are generated with CreateVersion7" (ADR 0039).
    /// </summary>
    public const string UserId = "019b4c2e-7a10-7d3f-8c5e-2b9a6f41d0e7";
}
