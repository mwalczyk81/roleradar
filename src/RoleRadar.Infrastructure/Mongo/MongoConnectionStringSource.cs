namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Resolves the MongoDB connection string from the environment, supporting the Docker-secrets
/// convention (<c>ROLERADAR_MONGO_CONNECTION_STRING_FILE</c>) in addition to the plain variable.
/// </summary>
public static class MongoConnectionStringSource
{
    public const string ConnectionStringVariable = "ROLERADAR_MONGO_CONNECTION_STRING";
    public const string ConnectionStringFileVariable = "ROLERADAR_MONGO_CONNECTION_STRING_FILE";

    public static string? Resolve(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var plain = getEnvironmentVariable(ConnectionStringVariable);
        var file = getEnvironmentVariable(ConnectionStringFileVariable);

        var hasPlain = !string.IsNullOrWhiteSpace(plain);
        var hasFile = !string.IsNullOrWhiteSpace(file);

        if (hasPlain && hasFile)
        {
            throw new InvalidOperationException(
                $"Both {ConnectionStringVariable} and {ConnectionStringFileVariable} are configured. Specify only one source.");
        }

        if (hasFile)
        {
            var filePath = file!.Trim();
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException(
                    $"{ConnectionStringFileVariable} points to a missing file: '{filePath}'.");
            }

            var content = File.ReadAllText(filePath).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    $"{ConnectionStringFileVariable} file '{filePath}' is empty.");
            }

            return content;
        }

        if (hasPlain)
        {
            return plain!.Trim();
        }

        return null;
    }
}
