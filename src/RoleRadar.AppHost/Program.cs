using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using RoleRadar.AppHost;

// Feature 015 Gate 5 (ADR 0049): the one-command local telemetry dev loop.
//   dotnet run --project src/RoleRadar.AppHost        (stop it with Ctrl+C, see specs/015-observability/quickstart.md)
// Identity, the API and the web app run from source next to a throwaway replica-set MongoDB, and the Aspire Dashboard
// shows their traces, structured logs and metrics. It is isolated from the developer's real stack: its own names, its own
// host ports, its own throwaway data, loopback HTTP only (no certificate has to be trusted). The packaged Compose stack
// stays exactly as it is.

// Fail closed before anything is created: no real-stack names, volumes or ports, and no host port that is already bound.
AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(AppHostSettings.CreatePlan());

var builder = DistributedApplication.CreateBuilder(args);

// Fictional secrets, generated per run and never written anywhere. Identity stores no client secret on disk.
var webClientSecret = RandomSecret(24);
var headlessClientSecret = RandomSecret(24);
var sessionEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var ownerSubject = Environment.GetEnvironmentVariable(AppHostSettings.OwnerSubjectVariable) is { Length: > 0 } supplied
    ? supplied
    : AppHostSettings.PlaceholderOwnerSubject;

var identityDataDirectory = AppHostSettings.IdentityDataDirectory();
Directory.CreateDirectory(identityDataDirectory);

// MongoDB 9.0.4 as a single-node replica set (rs0), initialised by the Aspire MongoDB integration, on a throwaway volume.
// WithReplicaSet is marked experimental by the library (ASPIREMONGODB001). Standards first (Principle X): using it beats a
// hand-rolled rs.initiate container, and the exact 13.6.5 pin means it cannot change under us. Only this call opts in.
#pragma warning disable ASPIREMONGODB001
// The fixed fictional password keeps the kept data volume and the root user in sync across stop and start.
var mongoPassword = builder.AddParameter(AppHostSettings.MongoPasswordParameterName, AppHostSettings.MongoPassword, secret: true);
var mongo = builder.AddMongoDB(AppHostSettings.MongoResourceName, port: AppHostSettings.MongoHostPort, password: mongoPassword)
    .WithImageTag(AppHostSettings.MongoImageTag)
    .WithEnvironment("GLIBC_TUNABLES", AppHostSettings.GlibcTunables)
    .WithReplicaSet(AppHostSettings.ReplicaSetName)
    .WithDataVolume(AppHostSettings.MongoDataVolumeName)
    .WithEndpoint("tcp", endpoint => endpoint.IsProxied = false) // Docker publishes 127.0.0.1:<MongoHostPort> itself
    .WithLifetime(ContainerLifetime.Session);
#pragma warning restore ASPIREMONGODB001

// Aspire starts a project without a launch profile as Production. Development is what lets the API accept the loopback http
// authority (its RequireHttpsMetadata is on outside Development) and what the Duende licence message allows.
var identity = builder.AddProject<Projects.RoleRadar_Identity>(AppHostSettings.IdentityResourceName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(port: AppHostSettings.IdentityHostPort, isProxied: false)
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ROLERADAR_IDENTITY_DB", $"Data Source={Path.Combine(identityDataDirectory, "identity.db")}")
    .WithEnvironment("ROLERADAR_IDENTITY_ISSUER_URI", AppHostSettings.IdentityIssuerUri)
    .WithEnvironment("Clients__Web__RedirectUri", AppHostSettings.WebRedirectUri)
    .WithEnvironment("Clients__Web__PostLogoutRedirectUri", AppHostSettings.WebPostLogoutRedirectUri)
    .WithEnvironment("ROLERADAR_WEB_CLIENT_SECRET", webClientSecret)
    .WithEnvironment("ROLERADAR_HEADLESS_CLIENT_SECRET", headlessClientSecret)
    .WithEnvironment("ROLERADAR_HEADLESS_ALLOWED_SUBJECT", AppHostSettings.HeadlessSubject);

var api = builder.AddProject<Projects.RoleRadar_Api>(AppHostSettings.ApiResourceName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(port: AppHostSettings.ApiHostPort, isProxied: false)
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ROLERADAR_MONGO_CONNECTION_STRING", mongo.Resource.ConnectionStringExpression)
    .WithEnvironment("ROLERADAR_MONGO_DATABASE", AppHostSettings.DatabaseName)
    .WithEnvironment("ROLERADAR_OIDC_AUTHORITY", AppHostSettings.IdentityIssuerUri)
    .WithEnvironment("ROLERADAR_OIDC_AUDIENCE", AppHostSettings.OidcAudience)
    .WithEnvironment("Owner__AllowedSubjects__0", ownerSubject)
    .WithEnvironment("Owner__AllowedSubjects__1", AppHostSettings.HeadlessSubject)
    .WaitFor(mongo)
    .WaitFor(identity);

// The web app runs the same server.ts as the packaged image (so its OpenTelemetry setup is the real one), over loopback
// HTTP. NODE_ENV=development is what lets server.ts fall back to HTTP and the session cookie drop its Secure attribute.
builder.AddJavaScriptApp(AppHostSettings.WebResourceName, "../../web", "apphost")
    .WithPnpm(install: true, installArgs: ["--frozen-lockfile"])
    .WithHttpEndpoint(port: AppHostSettings.WebHostPort, env: "PORT", isProxied: false)
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithEnvironment("HOST", "127.0.0.1")
    // server.ts auto-discovers deploy/certs/dev-cert.* and would serve HTTPS. Cert paths that do not exist (they sit in the
    // throwaway folder and are never created) make it fall back to HTTP, which is what keeps this loop certificate-free.
    .WithEnvironment("HTTPS_CERT_PATH", Path.Combine(identityDataDirectory, "no-tls", "cert.crt"))
    .WithEnvironment("HTTPS_CERT_KEY_PATH", Path.Combine(identityDataDirectory, "no-tls", "cert.key"))
    .WithEnvironment("NODE_ENV", "development")
    .WithEnvironment("COOKIE_SECURE", "false")
    .WithEnvironment("ROLERADAR_IDENTITY_ISSUER_URI", AppHostSettings.IdentityIssuerUri)
    .WithEnvironment("ROLERADAR_API_BASE_URL", AppHostSettings.ApiBaseUri)
    .WithEnvironment("ROLERADAR_WEB_CLIENT_SECRET", webClientSecret)
    .WithEnvironment("SESSION_ENCRYPTION_KEY", sessionEncryptionKey)
    .WithEnvironment("ROLERADAR_WEB_REDIRECT_URI", AppHostSettings.WebRedirectUri)
    .WithEnvironment("ROLERADAR_WEB_POST_LOGOUT_URI", AppHostSettings.WebPostLogoutRedirectUri)
    .WaitFor(identity)
    .WaitFor(api);

builder.Build().Run();

static string RandomSecret(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));
