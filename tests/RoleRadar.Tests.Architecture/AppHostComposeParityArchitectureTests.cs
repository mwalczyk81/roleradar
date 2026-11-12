namespace RoleRadar.Tests.Architecture;

using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Feature 015 Gate 5 (ADR 0049, SC-003, SC-006, SC-007): the local AppHost must stay the same stack as the packaged
/// Compose stack, and must never reach the developer's real stack. The rules read source text, so they need no Docker
/// and load no host. They compare service SHAPE (image, replica set, GLIBC_TUNABLES, required environment variables,
/// in-container ports), never host ports: the real dev stack already owns 27017 and 5001 to 5003 on the host, so the
/// AppHost publishes the same in-container ports on host ports shifted by a fixed offset.
/// Sentinels fail when an expected file is missing, so no rule can pass because the code is absent.
/// </summary>
public sealed partial class AppHostComposeParityArchitectureTests
{
    // The one approved dashboard reference (ADR 0053): 13.6.5 manifest-list digest, verified against the MCR registry.
    private const string ApprovedDashboardReference =
        "mcr.microsoft.com/dotnet/aspire-dashboard:13.6.5@sha256:e69a221d6d4ce3ee9bd74b9474c073dd154ef8df7eaa535c2f50f4a554adce54";

    private const string StaleDashboardDigest = "23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230";

    private static readonly string Root = LocateRepositoryRoot();

    // Real-stack names and ports the AppHost must never use (the developer's running stack).
    private static readonly string[] ForbiddenNames = ["roleradar-mongo", "roleradar-mongo-data", "roleradar-identity-data"];
    private static readonly int[] RealStackHostPorts = [27017, 5001, 5002, 5003];
    private static readonly string[] ComposedServices = ["identity", "api", "web"];
    private static readonly string[] IgnoredFolders = ["bin", "obj", "node_modules", ".git", ".nx", ".pnpm-store", "StrykerOutput", "TestResults", "dist"];

    // Compose variables that only make sense inside the hardened containers (TLS material, container hardening, health
    // loopback listener, production mode). Every other variable in the apps overlay must be wired by the AppHost.
    private static readonly string[] ContainerOnlyVariables =
    [
        "Kestrel__Endpoints__LoopbackHealth__Url",
        "Kestrel__Endpoints__Https__Url",
        "Kestrel__Endpoints__Https__Certificate__Path",
        "Kestrel__Endpoints__Https__Certificate__Password",
        "SSL_CERT_FILE",
        "NODE_EXTRA_CA_CERTS",
        "HTTPS_CERT_PATH",
        "HTTPS_CERT_KEY_PATH",
        "DOTNET_EnableDiagnostics",
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",
        "DOTNET_gcServer",
        "NODE_ENV",
    ];

    private static string AppHostDir => Path.Combine(Root, "src", "RoleRadar.AppHost");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string AppHostFile(string name) => Path.Combine(AppHostDir, name);

    private static string Settings => File.ReadAllText(AppHostFile("AppHostSettings.cs"));

    private static string Program => File.ReadAllText(AppHostFile("Program.cs"));

    private static string Guard => File.ReadAllText(AppHostFile("AppHostSafetyGuard.cs"));

    // ---- sentinels ----

    [Theory]
    [InlineData("RoleRadar.AppHost.csproj")]
    [InlineData("Program.cs")]
    [InlineData("AppHostSettings.cs")]
    [InlineData("AppHostSafetyGuard.cs")]
    [InlineData("Properties/launchSettings.json")]
    [InlineData("packages.lock.json")]
    public void Sentinel_TheAppHostFileExists(string file)
    {
        Assert.True(File.Exists(AppHostFile(file)), $"Expected src/RoleRadar.AppHost/{file} (Gate 5, ADR 0049).");
    }

    [Fact]
    public void Sentinel_TheComposeFilesExistAndDescribeTheServicesCompared()
    {
        var compose = Read("deploy", "docker-compose.yml");
        var apps = Read("deploy", "docker-compose.apps.yml");

        Assert.Contains("roleradar-mongo", compose, StringComparison.Ordinal);
        foreach (var service in new[] { "identity", "api", "web" })
        {
            Assert.NotEmpty(ComposeBlock(apps, service));
        }
    }

    // ---- MongoDB: image, replica set, GLIBC_TUNABLES, in-container port ----

    [Fact]
    public void MongoImage_MatchesCompose()
    {
        var composeImage = ComposeScalar(ComposeBlock(Read("deploy", "docker-compose.yml"), "mongo"), "image");
        Assert.Equal(composeImage, $"{Const(Settings, "MongoImage")}:{Const(Settings, "MongoImageTag")}");
    }

    [Fact]
    public void MongoReplicaSet_MatchesCompose_AndIsPassedToMongod()
    {
        var command = string.Join(' ', ComposeBlock(Read("deploy", "docker-compose.yml"), "mongo").Where(l => l.TrimStart().StartsWith("command:", StringComparison.Ordinal)));
        Assert.Matches(@"""--replSet"",\s*""rs0""", command);
        Assert.Equal("rs0", Const(Settings, "ReplicaSetName"));
        // The Aspire MongoDB integration passes --replSet to mongod and initialises the set (standards first: no hand-rolled init).
        Assert.Contains("WithReplicaSet(AppHostSettings.ReplicaSetName)", Program, StringComparison.Ordinal);
    }

    [Fact]
    public void MongoGlibcTunables_MatchCompose_AndAreSetOnTheContainer()
    {
        var env = ComposeEnvironment(ComposeBlock(Read("deploy", "docker-compose.yml"), "mongo"));
        Assert.Equal(env["GLIBC_TUNABLES"], Const(Settings, "GlibcTunables"));
        Assert.Contains("\"GLIBC_TUNABLES\"", Program, StringComparison.Ordinal);
        Assert.Contains("AppHostSettings.GlibcTunables", Program, StringComparison.Ordinal);
    }

    // ---- the kept data volume and the root password must always agree (stop and start has to work) ----

    [Fact]
    public void MongoPassword_IsOneFixedFictionalValue_SoTheKeptVolumeAlwaysMatchesIt()
    {
        // Aspire generates a new random root password on every run unless one is passed in, while WithDataVolume keeps the data
        // (and the root user created on the first start), so a second start was rejected with SCRAM AuthenticationFailed.
        var password = Const(Settings, "MongoPassword");
        Assert.StartsWith("fictional-", password, StringComparison.Ordinal);
        Assert.True(password.Length >= 16, "Keep the fictional password long enough that it is never mistaken for a placeholder.");

        Assert.Contains("AddParameter(AppHostSettings.MongoPasswordParameterName, AppHostSettings.MongoPassword, secret: true)", Program, StringComparison.Ordinal);
        Assert.Matches(@"AddMongoDB\(AppHostSettings\.MongoResourceName,[^)]*password:\s*mongoPassword", Program);
    }

    [Fact]
    public void MongoPassword_StaysInTheSettings_AndNeverAppearsInProgramOrTheLaunchProfile()
    {
        var password = Const(Settings, "MongoPassword");
        Assert.DoesNotContain(password, Program, StringComparison.Ordinal);
        Assert.DoesNotContain(password, File.ReadAllText(AppHostFile("Properties/launchSettings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheQuickstart_GivesTheFixedPassword_AndNoLongerSendsYouToReadAGeneratedOne()
    {
        var quickstart = Read("specs", "015-observability", "quickstart.md");
        Assert.DoesNotContain("MONGO_INITDB_ROOT_PASSWORD", quickstart, StringComparison.Ordinal);
        Assert.Contains(Const(Settings, "MongoPassword"), quickstart, StringComparison.Ordinal);
    }

    [Fact]
    public void MongoContainerPort_MatchesCompose_AndTheHostPortIsShiftedAwayFromTheRealStack()
    {
        var mongoPorts = ComposePorts(ComposeBlock(Read("deploy", "docker-compose.yml"), "mongo")).Single();
        var offset = ConstInt(Settings, "PortOffset");

        Assert.Equal(mongoPorts.Container, ConstInt(Settings, "MongoContainerPort"));
        Assert.Equal(mongoPorts.Container + offset, ConstInt(Settings, "MongoHostPort"));
        Assert.DoesNotContain(ConstInt(Settings, "MongoHostPort"), RealStackHostPorts);
    }

    // ---- services: in-container ports and required environment variables ----

    [Theory]
    [InlineData("identity", "IdentityHostPort")]
    [InlineData("api", "ApiHostPort")]
    [InlineData("web", "WebHostPort")]
    public void ServiceHostPort_IsTheComposeInContainerPortPlusTheOffset_AndNeverARealStackPort(string service, string settingName)
    {
        var composePort = ComposePorts(ComposeBlock(Read("deploy", "docker-compose.apps.yml"), service)).Single();
        var hostPort = ConstInt(Settings, settingName);

        Assert.Equal(composePort.Container + ConstInt(Settings, "PortOffset"), hostPort);
        Assert.DoesNotContain(hostPort, RealStackHostPorts);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("api")]
    [InlineData("web")]
    public void EveryComposeVariable_ThatIsNotContainerOnly_IsWiredByTheAppHost(string service)
    {
        var wiring = Program + Settings;
        var names = ComposeEnvironment(ComposeBlock(Read("deploy", "docker-compose.apps.yml"), service)).Keys
            .Where(name => !ContainerOnlyVariables.Contains(name, StringComparer.Ordinal))
            .ToArray();

        Assert.NotEmpty(names);
        var missing = names.Where(name => !wiring.Contains($"\"{name}\"", StringComparison.Ordinal)).ToArray();
        Assert.True(missing.Length == 0, $"The AppHost does not wire {service} variables present in docker-compose.apps.yml: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ContainerOnlyAllowlist_ContainsOnlyVariablesThatStillExistInTheComposeOverlay()
    {
        var apps = Read("deploy", "docker-compose.apps.yml");
        var all = ComposedServices
            .SelectMany(s => ComposeEnvironment(ComposeBlock(apps, s)).Keys)
            .ToHashSet(StringComparer.Ordinal);

        var stale = ContainerOnlyVariables.Where(v => !all.Contains(v)).ToArray();
        Assert.True(stale.Length == 0, $"Remove variables from the container-only allowlist that compose no longer sets: {string.Join(", ", stale)}");
    }

    // ---- OIDC consistency through configuration, over loopback HTTP (no certificate trust) ----

    [Fact]
    public void Issuer_RedirectUris_AndAuthority_AreDerivedFromTheSameHostPorts()
    {
        Assert.Matches(@"IdentityIssuerUri\s*=\s*\$""http://127\.0\.0\.1:\{IdentityHostPort\}""", Settings);
        Assert.Matches(@"ApiBaseUri\s*=\s*\$""http://127\.0\.0\.1:\{ApiHostPort\}""", Settings);
        Assert.Matches(@"WebRedirectUri\s*=\s*\$""http://127\.0\.0\.1:\{WebHostPort\}/auth/callback""", Settings);
        Assert.Matches(@"WebPostLogoutRedirectUri\s*=\s*\$""http://127\.0\.0\.1:\{WebHostPort\}/auth/logout""", Settings);

        foreach (var wired in new[]
        {
            "\"ROLERADAR_IDENTITY_ISSUER_URI\", AppHostSettings.IdentityIssuerUri",
            "\"ROLERADAR_OIDC_AUTHORITY\", AppHostSettings.IdentityIssuerUri",
            "\"Clients__Web__RedirectUri\", AppHostSettings.WebRedirectUri",
            "\"Clients__Web__PostLogoutRedirectUri\", AppHostSettings.WebPostLogoutRedirectUri",
            "\"ROLERADAR_WEB_REDIRECT_URI\", AppHostSettings.WebRedirectUri",
            "\"ROLERADAR_WEB_POST_LOGOUT_URI\", AppHostSettings.WebPostLogoutRedirectUri",
            "\"ROLERADAR_API_BASE_URL\", AppHostSettings.ApiBaseUri",
        })
        {
            Assert.Contains(wired, Program, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LaunchProfile_IsHttpOnly_LoopbackOnly_AndAllowsTheUnsecuredDashboardTransport()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AppHostFile("Properties/launchSettings.json")));
        var profiles = document.RootElement.GetProperty("profiles");
        Assert.NotEmpty(profiles.EnumerateObject());

        foreach (var profile in profiles.EnumerateObject())
        {
            var text = profile.Value.GetRawText();
            Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("localhost", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0.0.0.0", text, StringComparison.Ordinal);

            Assert.Matches(@"^http://127\.0\.0\.1:\d+$", profile.Value.GetProperty("applicationUrl").GetString());
            var env = profile.Value.GetProperty("environmentVariables");
            Assert.Equal("true", env.GetProperty("ASPIRE_ALLOW_UNSECURED_TRANSPORT").GetString());
            Assert.Matches(@"^http://127\.0\.0\.1:\d+$", env.GetProperty("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL").GetString());
            Assert.Matches(@"^http://127\.0\.0\.1:\d+$", env.GetProperty("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL").GetString());
            Assert.Matches(@"^http://127\.0\.0\.1:\d+$", env.GetProperty("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL").GetString());
        }
    }

    [Fact]
    public void LaunchProfile_PortsEqualTheSettings()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AppHostFile("Properties/launchSettings.json")));
        var profile = document.RootElement.GetProperty("profiles").EnumerateObject().Single().Value;
        var env = profile.GetProperty("environmentVariables");

        static int PortOf(string? url) => new Uri(url!).Port;

        Assert.Equal(ConstInt(Settings, "DashboardUiPort"), PortOf(profile.GetProperty("applicationUrl").GetString()));
        Assert.Equal(ConstInt(Settings, "DashboardOtlpGrpcPort"), PortOf(env.GetProperty("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL").GetString()));
        Assert.Equal(ConstInt(Settings, "DashboardOtlpHttpPort"), PortOf(env.GetProperty("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL").GetString()));
        Assert.Equal(ConstInt(Settings, "DashboardResourceServicePort"), PortOf(env.GetProperty("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL").GetString()));
    }

    [Fact]
    public void TheAspireSdk_IsPinnedInGlobalJson_AtTheWaivedSecurityFix()
    {
        using var document = JsonDocument.Parse(Read("global.json"));
        Assert.Equal("13.6.5", document.RootElement.GetProperty("msbuild-sdks").GetProperty("Aspire.AppHost.Sdk").GetString());
    }

    [Fact]
    public void NoCertificateTrustStep_ExistsInTheAppHostOrItsDocumentation()
    {
        var files = Directory.EnumerateFiles(AppHostDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.EndsWith("packages.lock.json", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var command in new[] { "dev-certs", "certutil", "Import-Certificate", "Cert:\\", "update-ca-certificates" })
            {
                Assert.DoesNotContain(command, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---- pins: Aspire packages, dashboard reference, audit ----

    [Fact]
    public void AspirePackages_ArePinnedExactlyAtTheWaivedSecurityFix()
    {
        var props = Read("Directory.Packages.props");
        foreach (var package in new[] { "Aspire.Hosting.AppHost", "Aspire.Hosting.MongoDB", "Aspire.Hosting.JavaScript" })
        {
            Assert.Contains($"<PackageVersion Include=\"{package}\" Version=\"13.6.5\" />", props, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("13.6.4", props, StringComparison.Ordinal);
    }

    [Fact]
    public void NoNuGetAuditSuppression_OrWarningSuppressionForAdvisories_ExistsAnywhere()
    {
        var offenders = Directory.EnumerateFiles(Root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".csproj", StringComparison.Ordinal) || f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal) || f.EndsWith("NuGet.Config", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutputOrDependencyFolder(f))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("NuGetAuditSuppress", StringComparison.Ordinal)
                       || Regex.IsMatch(text, @"<NoWarn>[^<]*NU190[0-9]")
                       || Regex.IsMatch(text, @"<WarningsNotAsErrors>[^<]*NU190[0-9]");
            })
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .ToArray();

        Assert.True(offenders.Length == 0, $"Advisories are never suppressed (ADR 0053). Offending files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void DashboardReference_IsTheApprovedPinEverywhereItIsDocumented()
    {
        var files = new[]
        {
            Path.Combine("specs", "015-observability", "tasks.md"),
            Path.Combine("specs", "015-observability", "plan.md"),
            Path.Combine("specs", "015-observability", "research.md"),
            Path.Combine("specs", "015-observability", "quickstart.md"),
            Path.Combine("specs", "015-observability", "contracts", "local-dev-loop.md"),
        };

        var occurrences = 0;
        foreach (var file in files)
        {
            var text = Read(file);
            Assert.DoesNotContain(StaleDashboardDigest, text, StringComparison.Ordinal);
            foreach (Match match in DashboardReferenceRegex().Matches(text))
            {
                occurrences++;
                Assert.Equal(ApprovedDashboardReference, match.Value);
            }
        }

        Assert.True(occurrences >= 4, $"Expected the approved dashboard reference in the Gate 5 documents (found {occurrences}).");
    }

    [Fact]
    public void Adr0049_IsAmendedNotRewritten_AndAdr0053RecordsTheWaiver()
    {
        var adr49 = Read("docs", "adr", "0049-local-observability-apphost-and-dashboard.md");
        Assert.Contains(StaleDashboardDigest, adr49, StringComparison.Ordinal); // history stays
        Assert.Contains("## Addendum", adr49, StringComparison.Ordinal);
        Assert.Contains(ApprovedDashboardReference, adr49, StringComparison.Ordinal);

        var adr53 = Read("docs", "adr", "0053-aspire-13-6-5-security-fix-release-age-waiver.md");
        Assert.Contains("GHSA-f7vh-h5g3-7jrh", adr53, StringComparison.Ordinal);
        Assert.Contains("CVE-2026-120006", adr53, StringComparison.Ordinal);
        Assert.Contains("2026-11-24", adr53, StringComparison.Ordinal);
        Assert.Contains("NU1903", adr53, StringComparison.Ordinal);
        Assert.Contains(ApprovedDashboardReference, adr53, StringComparison.Ordinal);
    }

    // ---- real-volume isolation and the fail-closed guard ----

    [Fact]
    public void AppHostCode_OutsideTheGuard_NeverNamesARealStackResource()
    {
        foreach (var file in new[] { "Program.cs", "AppHostSettings.cs", "Properties/launchSettings.json" })
        {
            var text = File.ReadAllText(AppHostFile(file));
            foreach (var forbidden in ForbiddenNames)
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void AppHostCode_NeverUses27017AsAHostPort_AndOnlyTheContainerPortConstantMentionsIt()
    {
        Assert.DoesNotContain("27017", Program, StringComparison.Ordinal);
        Assert.DoesNotContain("27017", File.ReadAllText(AppHostFile("Properties/launchSettings.json")), StringComparison.Ordinal);

        var settingsLines = Settings.Split('\n').Where(l => l.Contains("27017", StringComparison.Ordinal)).ToArray();
        Assert.All(settingsLines, line => Assert.Contains("MongoContainerPort", line, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRoleRadarNameInTheSettings_UsesTheThrowawayPrefix()
    {
        var literals = Regex.Matches(Settings, @"""(roleradar-[^""]*)""")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(literals);
        Assert.All(literals, name => Assert.StartsWith("roleradar-apphost-", name, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGuard_ActivelyRejectsTheRealStack_AndRefusesBoundPorts()
    {
        var guard = Guard;
        foreach (var forbidden in ForbiddenNames.Concat(["27017"]))
        {
            Assert.Contains(forbidden, guard, StringComparison.Ordinal);
        }

        Assert.Contains("InvalidOperationException", guard, StringComparison.Ordinal);
        Assert.Contains("GetActiveTcpListeners", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuard_RunsBeforeAnyResourceIsDeclared()
    {
        var program = Program;
        var guard = program.IndexOf("AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(", StringComparison.Ordinal);
        var firstResource = program.IndexOf("CreateBuilder(", StringComparison.Ordinal);

        Assert.True(guard >= 0, "Program.cs must call AppHostSafetyGuard.AssertIsolatedAppHostEnvironment.");
        Assert.True(firstResource > guard, "The guard must run before the distributed application builder is created.");
    }

    private static bool IsBuildOutputOrDependencyFolder(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return IgnoredFolders
            .Any(folder => path.Contains($"{separator}{folder}{separator}", StringComparison.Ordinal));
    }

    // ---- tiny readers (no YAML library: the compose files are flat and the rules need only a few facts) ----

    private static string[] ComposeBlock(string yaml, string service)
    {
        var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => l == $"  {service}:");
        if (start < 0)
        {
            return [];
        }

        var block = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length > 0 && !line.StartsWith("    ", StringComparison.Ordinal) && !line.StartsWith('#'))
            {
                break; // next 2-space service key, or a top-level key
            }

            block.Add(line);
        }

        return [.. block];
    }

    private static string ComposeScalar(string[] block, string key) =>
        block.Select(l => l.Trim()).Single(l => l.StartsWith($"{key}:", StringComparison.Ordinal))[(key.Length + 1)..].Trim();

    private static Dictionary<string, string> ComposeEnvironment(string[] block)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var inEnvironment = false;
        foreach (var raw in block)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("    environment:", StringComparison.Ordinal))
            {
                inEnvironment = true;
                continue;
            }

            if (!inEnvironment)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            var entry = trimmed[2..];
            var eq = entry.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                result[entry[..eq]] = entry[(eq + 1)..];
            }
        }

        return result;
    }

    private static (int Host, int Container)[] ComposePorts(string[] block)
    {
        var result = new List<(int, int)>();
        var inPorts = false;
        foreach (var raw in block)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("    ports:", StringComparison.Ordinal))
            {
                inPorts = true;
                continue;
            }

            if (!inPorts)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            var parts = trimmed[2..].Trim('"').Split(':');
            result.Add((int.Parse(parts[^2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture)));
        }

        return [.. result];
    }

    private static string Const(string source, string name)
    {
        var match = Regex.Match(source, $@"const\s+string\s+{name}\s*=\s*""([^""]*)""\s*;");
        Assert.True(match.Success, $"AppHostSettings.cs must declare: const string {name} = \"...\";");
        return match.Groups[1].Value;
    }

    private static int ConstInt(string source, string name)
    {
        var match = Regex.Match(source, $@"const\s+int\s+{name}\s*=\s*(\d+)\s*;");
        Assert.True(match.Success, $"AppHostSettings.cs must declare: const int {name} = <literal>;");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"mcr\.microsoft\.com/dotnet/aspire-dashboard:[0-9.]+@sha256:[0-9a-f]{64}")]
    private static partial Regex DashboardReferenceRegex();

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RoleRadar.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
