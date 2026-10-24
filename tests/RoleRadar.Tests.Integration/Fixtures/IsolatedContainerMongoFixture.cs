using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace RoleRadar.Tests.Integration.Fixtures;

/// <summary>
/// Starts an isolated Docker MongoDB replica set on an ephemeral port, dedicated network,
/// dedicated volume, and dedicated project label. Completely isolates container tests from
/// the host's dev stack (never touches 127.0.0.1:27017 or roleradar compose services).
/// </summary>
public sealed class IsolatedContainerMongoFixture : IAsyncLifetime
{
    public string TestId { get; } = Guid.NewGuid().ToString("N");
    public string ProjectName => $"rrtest-proj-{TestId}";
    public string ContainerName => $"rrtest-mongo-{TestId}";
    public string VolumeName => $"rrtest-vol-{TestId}";
    public string NetworkName => $"rrtest-net-{TestId}";
    public int HostPort { get; private set; }

    public string HostMongoUri => $"mongodb://127.0.0.1:{HostPort}/?replicaSet=rs0&directConnection=true";
    public string ContainerMongoUri => "mongodb://host.docker.internal:27018/?replicaSet=rs0&directConnection=true";

    public async ValueTask InitializeAsync()
    {
        HostPort = GetFreePort();

        // 0. Clean up any prior stale residue
        try { await RunProcessAsync("docker", $"rm -f {ContainerName}", 10); } catch { }
        try { await RunProcessAsync("docker", $"volume rm -f {VolumeName}", 10); } catch { }
        try { await RunProcessAsync("docker", $"network rm {NetworkName}", 10); } catch { }

        // 1. Create dedicated network
        await RunProcessAsync("docker", $"network create {NetworkName}", 30);

        // 2. Create dedicated volume
        await RunProcessAsync("docker", $"volume create --label com.docker.compose.project={ProjectName} {VolumeName}", 30);

        // 3. Launch isolated container
        await RunProcessAsync(
            "docker",
            $"run -d --name {ContainerName} --network {NetworkName} --network-alias host.docker.internal --network-alias mongo --label com.docker.compose.project={ProjectName} -p 127.0.0.1:{HostPort}:27018 -v {VolumeName}:/data/db mongo:8.0.34 mongod --replSet rs0 --port 27018 --bind_ip 127.0.0.1,host.docker.internal,mongo",
            60);

        // 4. Wait for mongo to respond and initiate single-node replica set
        await WaitForReplicaSetReadyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try { await RunProcessAsync("docker", $"rm -f {ContainerName}", 30); } catch { }
        try { await RunProcessAsync("docker", $"volume rm -f {VolumeName}", 30); } catch { }
        try { await RunProcessAsync("docker", $"network rm {NetworkName}", 30); } catch { }
    }

    private async Task WaitForReplicaSetReadyAsync()
    {
        var client = new MongoClient(HostMongoUri);
        var admin = client.GetDatabase("admin");

        // Wait for mongod process to accept connections
        var connected = false;
        for (var i = 0; i < 30; i++)
        {
            try
            {
                await admin.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                connected = true;
                break;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        if (!connected)
        {
            throw new InvalidOperationException($"Isolated MongoDB container '{ContainerName}' failed to accept connections on port {HostPort}.");
        }

        // Initiate replica set
        try
        {
            await admin.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                ["replSetInitiate"] = new BsonDocument
                {
                    ["_id"] = "rs0",
                    ["members"] = new BsonArray
                    {
                        new BsonDocument { ["_id"] = 0, ["host"] = "127.0.0.1:27018" }
                    }
                }
            });
        }
        catch (MongoCommandException ex) when (ex.CodeName == "AlreadyInitialized")
        {
            // Already initialized
        }

        // Wait for primary election
        var primaryReady = false;
        for (var i = 0; i < 30; i++)
        {
            try
            {
                var hello = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
                if (hello.TryGetValue("isWritablePrimary", out var isPrimary) && isPrimary.AsBoolean)
                {
                    primaryReady = true;
                    break;
                }
            }
            catch
            {
                // Wait for election
            }

            await Task.Delay(500);
        }

        if (!primaryReady)
        {
            throw new InvalidOperationException($"Isolated MongoDB container '{ContainerName}' did not achieve writable primary status.");
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task RunProcessAsync(string fileName, string arguments, int timeoutSeconds)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        proc.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process '{fileName} {arguments}' timed out after {timeoutSeconds}s.");
        }

        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            throw new InvalidOperationException($"Process '{fileName} {arguments}' failed with exit code {proc.ExitCode}.\nStdErr: {stderr}\nStdOut: {stdout}");
        }
    }
}
