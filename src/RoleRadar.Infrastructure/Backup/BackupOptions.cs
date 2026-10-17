namespace RoleRadar.Infrastructure.Backup;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string DumpCommand { get; set; } = "docker compose exec -T mongo mongodump";

    public string RestoreCommand { get; set; } = "docker compose exec -T mongo mongorestore";
}
