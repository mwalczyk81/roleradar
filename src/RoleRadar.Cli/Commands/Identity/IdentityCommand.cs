using System.CommandLine;

namespace RoleRadar.Cli.Commands.Identity;

public static class IdentityCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("identity", "Manage RoleRadar identity host and owner credentials.");
        command.Add(CreateOwnerCommand.Create(services));
        return command;
    }
}
