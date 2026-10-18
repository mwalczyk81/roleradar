using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;

namespace RoleRadar.Cli.Commands;

public static class ContactCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var contactCommand = new Command("contact", "Manage confidential third-party professional contacts (CLI-only)");

        contactCommand.Add(CreateAddCommand(services));
        contactCommand.Add(CreateGetCommand(services));
        contactCommand.Add(CreateRedactCommand(services));

        return contactCommand;
    }

    private static Command CreateAddCommand(IServiceProvider services)
    {
        var cmd = new Command("add", "Add a new confidential professional contact");

        var nameOption = new Option<string>("--name") { Description = "Contact full name (1..150 chars)", Required = true };
        var roleOption = new Option<string>("--role") { Description = "Contact job role/title (max 150 chars)" };
        var companyOption = new Option<string>("--company") { Description = "Company affiliation name (max 150 chars)" };
        var companyIdOption = new Option<Guid?>("--company-id") { Description = "Company binary GUID reference" };
        var relationshipOption = new Option<string>("--relationship") { Description = "Relationship category (recruiter, referral, hiring_manager, peer)", DefaultValueFactory = _ => "peer" };
        var notesOption = new Option<string>("--notes") { Description = "Discussion notes (max 500 chars)" };
        var jsonOption = new Option<bool>("--json") { Description = "Emit structured JSON output" };

        cmd.Add(nameOption);
        cmd.Add(roleOption);
        cmd.Add(companyOption);
        cmd.Add(companyIdOption);
        cmd.Add(relationshipOption);
        cmd.Add(notesOption);
        cmd.Add(jsonOption);

        cmd.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetValue(nameOption)!;
            var role = parseResult.GetValue(roleOption);
            var company = parseResult.GetValue(companyOption);
            var companyId = parseResult.GetValue(companyIdOption);
            var relationship = parseResult.GetValue(relationshipOption);
            var notes = parseResult.GetValue(notesOption);
            var json = parseResult.GetValue(jsonOption);

            using var scope = services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AddContactCommand, ContactResultDto>>();

            var command = new AddContactCommand(
                Name: name,
                Role: role,
                Company: company,
                CompanyId: companyId,
                Relationship: relationship,
                Notes: notes,
                Actor: "cli-user");

            var result = await handler.HandleAsync(command, ct);
            if (result.IsSuccess && result.Value is not null)
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data = result.Value }, JsonOpts));
                }
                else
                {
                    Console.WriteLine($"Contact created: {result.Value.Id}");
                    Console.WriteLine($"Name:         {result.Value.Name}");
                    Console.WriteLine($"Role:         {result.Value.Role ?? "(none)"}");
                    Console.WriteLine($"Company:      {result.Value.CompanyAffiliation ?? "(none)"}");
                    Console.WriteLine($"Relationship: {result.Value.Relationship}");
                }
                return 0;
            }

            var err = result.Error!.Value;
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code = err.Code.ToString(), message = err.Message } }, JsonOpts));
            }
            else
            {
                Console.Error.WriteLine($"Error: {err.Message}");
            }
            return 2;
        });

        return cmd;
    }

    private static Command CreateGetCommand(IServiceProvider services)
    {
        var cmd = new Command("get", "Get contact details by contact ID strictly");

        var idOption = new Option<Guid>("--id") { Description = "Contact binary GUID identifier", Required = true };
        var jsonOption = new Option<bool>("--json") { Description = "Emit structured JSON output" };

        cmd.Add(idOption);
        cmd.Add(jsonOption);

        cmd.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(idOption);
            var json = parseResult.GetValue(jsonOption);

            using var scope = services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetContactQuery, ContactDto?>>();

            var query = new GetContactQuery(id);
            var result = await handler.HandleAsync(query, ct);

            if (result.IsSuccess && result.Value is not null)
            {
                var contact = result.Value;
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data = contact }, JsonOpts));
                }
                else
                {
                    Console.WriteLine($"Contact:      {contact.Id}");
                    Console.WriteLine($"Name:         {contact.Name}");
                    Console.WriteLine($"Role:         {contact.Role ?? "(none)"}");
                    Console.WriteLine($"Company:      {contact.CompanyAffiliation ?? "(none)"}");
                    Console.WriteLine($"Relationship: {contact.Relationship}");
                    Console.WriteLine($"Redacted:     {contact.IsRedacted}");
                }
                return 0;
            }

            var err = result.Error ?? new ResultError(ErrorCode.NotFound, $"Contact '{id}' not found.");
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code = err.Code.ToString(), message = err.Message } }, JsonOpts));
            }
            else
            {
                Console.Error.WriteLine($"Error: {err.Message}");
            }
            return 1;
        });

        return cmd;
    }

    private static Command CreateRedactCommand(IServiceProvider services)
    {
        var cmd = new Command("redact", "Redact all personal fields from a contact record");

        var idOption = new Option<Guid>("--id") { Description = "Contact binary GUID identifier", Required = true };
        var versionTokenOption = new Option<Guid?>("--version-token") { Description = "Optimistic concurrency version token" };
        var forceOption = new Option<bool>("--force") { Description = "Bypass interactive confirmation" };
        var jsonOption = new Option<bool>("--json") { Description = "Emit structured JSON output" };

        cmd.Add(idOption);
        cmd.Add(versionTokenOption);
        cmd.Add(forceOption);
        cmd.Add(jsonOption);

        cmd.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(idOption);
            var versionToken = parseResult.GetValue(versionTokenOption);
            var json = parseResult.GetValue(jsonOption);

            using var scope = services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RedactContactCommand, ContactRedactResultDto>>();

            var command = new RedactContactCommand(id, versionToken, Actor: "cli-user");
            var result = await handler.HandleAsync(command, ct);

            if (result.IsSuccess && result.Value is not null)
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data = result.Value }, JsonOpts));
                }
                else
                {
                    Console.WriteLine($"Contact redacted: {result.Value.Id} (status: {result.Value.Status})");
                }
                return 0;
            }

            var err = result.Error!.Value;
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code = err.Code.ToString(), message = err.Message } }, JsonOpts));
            }
            else
            {
                Console.Error.WriteLine($"Error: {err.Message}");
            }
            return 2;
        });

        return cmd;
    }
}
