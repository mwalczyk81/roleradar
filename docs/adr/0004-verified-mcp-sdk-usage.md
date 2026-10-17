# ADR 0004: Live-Verified NuGet Package & Docker Image Pins and API Signatures

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

Constitution Principle VIII requires explicitly pinning all NuGet package versions and container image tags and recording library decisions in MADR format. Relying on remembered package versions causes build failures, security audit warnings (`NuGetAudit`), or use of deprecated beta APIs (such as the 2022 `System.CommandLine` beta).

At plan time (2026-10-17), we queried `https://api.nuget.org/v3-flatcontainer/<id>/index.json` and `https://hub.docker.com/v2/repositories/library/mongo/tags` live and inspected the downloaded assemblies and XML documentation to verify exact stable versions and API signatures.

## Decision Outcome

### 1. Live-Verified Version Pins (`2026-10-17`)

| Dependency | Pinned Version | Target / Notes |
|------------|----------------|----------------|
| **.NET SDK (`global.json`)** | `10.0.402` | `rollForward: "latestPatch"` |
| **MongoDB Container Image** | `mongo:8.0.34` | Latest stable `8.0.x` patch tag on Docker Hub |
| **`ModelContextProtocol`** | `2.2.0` | Official C# MCP SDK (`net10.0` verified in `spikes/StdioEcho`) |
| **`Microsoft.Extensions.Hosting`** | `10.0.13` | Latest stable `10.0.x` release on NuGet.org |
| **`MongoDB.Driver`** | `3.12.0` | Latest stable `3.x` release on NuGet.org (Apache-2.0) |
| **`System.CommandLine`** | `2.0.13` | Stable `2.0.x` GA release on NuGet.org (replaces 2022 `2.0.0-beta4`) |
| **`Geralt`** | `4.4.0` | Latest stable MIT-licensed libsodium wrapper (`libsodium` `1.0.22`) |
| **`xunit.v3`** | `4.0.2` | Adopted as test framework (`net8.0`+, `Microsoft.Testing.Platform` v2) |
| **`xunit.runner.visualstudio`** | `4.0.1` | Latest stable runner for `xunit.v3` |
| **`Microsoft.NET.Test.Sdk`** | `18.11.0` | Latest stable test SDK on NuGet.org |
| **`Testcontainers.MongoDb`** | `4.16.0` | Latest stable release (`net10.0` native target) |
| **`NetArchTest.Rules`** | `1.3.2` | Architecture and dependency rule assertions |
| **`coverlet.collector`** | `10.1.0` | Latest stable code coverage collector |

---

### 2. Verified API Signatures

#### A. `ModelContextProtocol` `2.2.0` + `Microsoft.Extensions.Hosting` `10.0.13`

```csharp
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<NoteMcpTools>();

await builder.Build().RunAsync();
```

#### B. `System.CommandLine` `2.0.13` (Stable GA API — NOT 2022 beta)

```csharp
using System.CommandLine;

var rootCommand = new RootCommand("RoleRadar local CLI");
var importCommand = new Command("import", "Import UTF-8 markdown files into RoleRadar notes");

var filesArgument = new Argument<string[]>("files") { Arity = ArgumentArity.OneOrMore };
var areaOption = new Option<string>("--area") { DefaultValueFactory = _ => "imported" };
var defaultDateOption = new Option<string?>("--default-date");
var jsonOption = new Option<bool>("--json");

importCommand.Add(filesArgument);
importCommand.Add(areaOption);
importCommand.Add(defaultDateOption);
importCommand.Add(jsonOption);

importCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    string[] files = parseResult.GetRequiredValue(filesArgument);
    string area = parseResult.GetValue(areaOption)!;
    string? defaultDate = parseResult.GetValue(defaultDateOption);
    bool json = parseResult.GetValue(jsonOption);
    // Invoke injected ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>
    return 0;
});

rootCommand.Add(importCommand);
return await rootCommand.Parse(args).InvokeAsync(cancellationToken: CancellationToken.None);
```

#### C. `MongoDB.Driver` `3.12.0` + `Testcontainers.MongoDb` `4.16.0`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Testcontainers.MongoDb;

// Global one-time registration at startup:
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

// Ephemeral replica-set container in contract & integration tests:
await using MongoDbContainer container = new MongoDbBuilder("mongo:8.0.34")
    .WithReplicaSet("rs0")
    .Build();
await container.StartAsync(ct);
string connectionString = container.GetConnectionString();
```

#### D. `Geralt` `4.4.0` (`Argon2id` + `IncrementalXChaCha20Poly1305`)

```csharp
using Geralt;

// Constants verified via assembly reflection:
// Argon2id.KeySize = 32, Argon2id.SaltSize = 16
// IncrementalXChaCha20Poly1305.KeySize = 32, HeaderSize = 24, TagSize = 17
// IncrementalXChaCha20Poly1305.ChunkFlag: Message = 0, Boundary = 1, Rekey = 2, Final = 3

Span<byte> key = stackalloc byte[Argon2id.KeySize];
Argon2id.DeriveKey(key, passphraseUtf8Bytes, salt16Bytes, iterations: 4, memorySize: 262_144);

using var pushStream = new IncrementalXChaCha20Poly1305(header24Bytes, key, encryption: true);
pushStream.EncryptChunk(ciphertextChunk, plaintextChunk, associatedDataHeader,
    isLastChunk ? IncrementalXChaCha20Poly1305.ChunkFlag.Final : IncrementalXChaCha20Poly1305.ChunkFlag.Message);
SecureMemory.ZeroMemory(key);
```

### Consequences

- **Good**: Zero stale beta dependencies; `dotnet restore --locked-mode` and `NuGetAudit` pass cleanly against current stable packages.
- **Good**: Developers and agents have the exact, verified `System.CommandLine` `2.0.13` and `Geralt` `4.4.0` signatures in the repository.
