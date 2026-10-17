namespace RoleRadar.Infrastructure.Backup;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string DumpCommand { get; set; } = $"docker compose -f \"{ResolveDefaultComposePath()}\" exec -T mongo mongodump --archive --gzip --oplog";

    public string RestoreCommand { get; set; } = $"docker compose -f \"{ResolveDefaultComposePath()}\" exec -T mongo mongorestore --archive --gzip --oplogReplay --nsInclude \"<db>.*\"";

    public static string ResolveDefaultComposePath()
    {
        var envPath = Environment.GetEnvironmentVariable("ROLERADAR_COMPOSE_FILE");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath.Trim();
        }

        // Check relative to current working directory
        var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "deploy", "docker-compose.yml");
        if (File.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        // Walk up from AppContext.BaseDirectory
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "deploy", "docker-compose.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            currentDir = currentDir.Parent;
        }

        return "deploy/docker-compose.yml";
    }
}
