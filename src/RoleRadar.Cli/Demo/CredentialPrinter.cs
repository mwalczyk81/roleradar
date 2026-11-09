namespace RoleRadar.Cli.Demo;

/// <summary>
/// Prints the demo credentials once, to an interactive terminal only. When output or input is redirected (a file, a
/// pipe, a log driver) it prints nothing, so a secret cannot end up in a file or a log. It writes to the supplied
/// <see cref="TextWriter"/> only and uses no logger.
/// </summary>
public static class CredentialPrinter
{
    public static bool TryPrint(DemoCredentials credentials, TextWriter writer, Func<bool> isInteractive)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(isInteractive);

        if (!isInteractive())
        {
            return false;
        }

        writer.WriteLine();
        writer.WriteLine("RoleRadar demo sign-in (throwaway account, demo stack only)");
        writer.WriteLine(new string('-', 62));
        writer.WriteLine($"Username:    {credentials.Username}");
        writer.WriteLine($"Password:    {credentials.Password}");
        writer.WriteLine($"TOTP secret: {credentials.AuthenticatorKey}");
        writer.WriteLine($"OtpAuth URI: {credentials.OtpAuthUri}");
        writer.WriteLine(new string('-', 62));
        writer.WriteLine("These are shown only once and are not saved anywhere. Add the TOTP secret to an authenticator app,");
        writer.WriteLine("or print the current code with: roleradar demo totp");
        writer.WriteLine("Running this command again resets the account and prints new values.");
        writer.WriteLine();
        return true;
    }
}
