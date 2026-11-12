namespace RoleRadar.AppHost;

/// <summary>
/// Every name, port and pin the local dev loop uses (feature 015 Gate 5, ADR 0049). Plain constants with no Aspire
/// dependency, so the guard and the unit tests can use them and the architecture tests can read them as text.
/// The loop mirrors the packaged Compose stack's SHAPE (image, replica set, GLIBC_TUNABLES, in-container ports, required
/// variables) but publishes on host ports shifted by <see cref="PortOffset"/>, because the developer's real stack already
/// owns the Mongo and web ports. Everything runs over loopback HTTP, so no certificate has to be trusted.
/// </summary>
internal static class AppHostSettings
{
    // ---- isolation ----

    /// <summary>Every container, resource and volume of the loop starts with this. The guard enforces it.</summary>
    public const string ResourcePrefix = "roleradar-apphost-";

    public const string MongoResourceName = "roleradar-apphost-mongo";
    public const string IdentityResourceName = "roleradar-apphost-identity";
    public const string ApiResourceName = "roleradar-apphost-api";
    public const string WebResourceName = "roleradar-apphost-web";

    public const string MongoPasswordParameterName = "roleradar-apphost-mongo-password";
    public const string MongoDataVolumeName = "roleradar-apphost-mongo-data";
    public const string IdentityDataDirectoryName = "roleradar-apphost-identity-data";

    // ---- MongoDB: the same shape as deploy/docker-compose.yml ----

    public const string MongoImage = "mongo";
    public const string MongoImageTag = "9.0.4";
    public const string ReplicaSetName = "rs0";
    public const string GlibcTunables = "glibc.pthread.rseq=1";
    public const string DatabaseName = "roleradar";

    /// <summary>
    /// The root password of the throwaway Mongo: one fixed, fictional, loopback-only value. Aspire generates a new random
    /// password on every run unless one is passed in, but the data volume keeps the root user created on the first start, so a
    /// random password made every second start fail with SCRAM AuthenticationFailed. The container is published on 127.0.0.1
    /// only and holds fictional data only.
    /// </summary>
    public const string MongoPassword = "fictional-apphost-root-pw";

    /// <summary>The port mongod listens on inside the container (the same as Compose).</summary>
    public const int MongoContainerPort = 27017;

    // ---- host ports: Compose's in-container port + PortOffset ----

    public const int PortOffset = 10000;
    public const int MongoHostPort = 37017;
    public const int IdentityHostPort = 15001;
    public const int ApiHostPort = 15002;
    public const int WebHostPort = 15003;

    // The Dashboard (launchSettings.json carries the same numbers; an architecture test keeps them equal).
    public const int DashboardUiPort = 15888;
    public const int DashboardOtlpGrpcPort = 15889;
    public const int DashboardOtlpHttpPort = 15890;
    public const int DashboardResourceServicePort = 15891;

    // ---- OIDC, derived from the host ports so issuer, authority and redirect URIs cannot disagree ----

    public static readonly string IdentityIssuerUri = $"http://127.0.0.1:{IdentityHostPort}";
    public static readonly string ApiBaseUri = $"http://127.0.0.1:{ApiHostPort}";
    public static readonly string WebRedirectUri = $"http://127.0.0.1:{WebHostPort}/auth/callback";
    public static readonly string WebPostLogoutRedirectUri = $"http://127.0.0.1:{WebHostPort}/auth/logout";
    public const string OidcAudience = "roleradar.api";

    // ---- fictional identities (nothing real, ever) ----

    public const string HeadlessSubject = "roleradar-apphost-headless";

    /// <summary>Set to the sub printed by <c>create-owner</c> so the API accepts that fictional owner's tokens.</summary>
    public const string OwnerSubjectVariable = "ROLERADAR_APPHOST_OWNER_SUBJECT";

    /// <summary>A placeholder that matches no account, so the API starts closed until the fictional owner's sub is supplied.</summary>
    public const string PlaceholderOwnerSubject = "00000000-0000-0000-0000-000000000000";

    /// <summary>The host folder holding the throwaway Identity database and keys. Never the real stack's Identity volume.</summary>
    public static string IdentityDataDirectory() => Path.Combine(Path.GetTempPath(), IdentityDataDirectoryName);

    public static AppHostPlan CreatePlan() => new(
        Names:
        [
            MongoResourceName,
            MongoPasswordParameterName,
            IdentityResourceName,
            ApiResourceName,
            WebResourceName,
        ],
        VolumeNames: [MongoDataVolumeName, IdentityDataDirectoryName],
        HostPorts:
        [
            MongoHostPort,
            IdentityHostPort,
            ApiHostPort,
            WebHostPort,
            DashboardUiPort,
            DashboardOtlpGrpcPort,
            DashboardOtlpHttpPort,
            DashboardResourceServicePort,
        ],
        ConnectionStrings: [$"mongodb://127.0.0.1:{MongoHostPort}/?directConnection=true"]);
}
