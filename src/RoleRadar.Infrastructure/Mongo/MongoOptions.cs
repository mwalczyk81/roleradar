namespace RoleRadar.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    // Defaults to single-node loopback replica set with directConnection=true so host clients connect directly without container-hostname discovery.
    public string ConnectionString { get; set; } = "mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true";

    public string DatabaseName { get; set; } = "roleradar";

    public string ReplicaSetName => "rs0";

    public TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(3);
}
