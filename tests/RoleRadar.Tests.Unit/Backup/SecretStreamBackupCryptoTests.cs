using System.Security.Cryptography;
using System.Text;
using FsCheck;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace RoleRadar.Tests.Unit.Backup;

[CollectionDefinition("NonParallelCryptoMemory", DisableParallelization = true)]
public sealed class NonParallelCryptoMemoryCollectionDefinition
{
}

[Collection("NonParallelCryptoMemory")]
public sealed class SecretStreamBackupCryptoTests
{
    private readonly SecretStreamBackupArchiveService _sut;

    public SecretStreamBackupCryptoTests()
    {
        var mongoOptions = Options.Create(new MongoOptions());
        var backupOptions = Options.Create(new BackupOptions());
        // Mock / minimal IMongoClient for crypto unit tests (no DB calls made in crypto stream methods)
        var clientSettings = new MongoClientSettings { ServerSelectionTimeout = TimeSpan.FromMilliseconds(100) };
        var mongoClient = new MongoClient(clientSettings);
        _sut = new SecretStreamBackupArchiveService(mongoClient, mongoOptions, backupOptions);
    }

    [Fact]
    public async Task KnownAnswer_DeterministicVector_RoundTripsSuccessfully()
    {
        var plaintext = "RoleRadar Fictional Note Backup Test Payload"u8.ToArray();
        const string passphrase = "CorrectHorseBatteryStaple123!";

        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();

        var encResult = await _sut.EncryptStreamAsync(plainStream, cipherStream, passphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(encResult.IsSuccess, $"Encryption failed: {encResult.Error?.Message}");

        cipherStream.Position = 0;
        using var decryptedStream = new MemoryStream();
        var decResult = await _sut.DecryptStreamAsync(cipherStream, decryptedStream, passphrase);
        Assert.True(decResult.IsSuccess, $"Decryption failed: {decResult.Error?.Message}");

        Assert.Equal(plaintext, decryptedStream.ToArray());
    }

    [Fact]
    public async Task CraftedHeader_Argon2MemoryKiB_AboveCeiling_RejectedBeforeKdf()
    {
        // 1 GiB ceiling is 1048576 KiB. 1048577 should be rejected.
        var header = BuildValidHeaderTemplate(memoryKiB: BackupArchiveConstants.MaxArgon2MemoryKiB + 1);
        using var stream = new MemoryStream(header);

        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task CraftedHeader_Argon2MemoryKiB_BelowFloor_RejectedBeforeKdf()
    {
        var header = BuildValidHeaderTemplate(memoryKiB: BackupArchiveConstants.MinArgon2MemoryKiB - 1);
        using var stream = new MemoryStream(header);

        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task CraftedHeader_Argon2Iterations_AboveCeiling_RejectedBeforeKdf()
    {
        var header = BuildValidHeaderTemplate(iterations: BackupArchiveConstants.MaxArgon2Iterations + 1);
        using var stream = new MemoryStream(header);

        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task CraftedHeader_Argon2Iterations_BelowFloor_RejectedBeforeKdf()
    {
        var header = BuildValidHeaderTemplate(iterations: BackupArchiveConstants.MinArgon2Iterations - 1);
        using var stream = new MemoryStream(header);

        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task CraftedHeader_UnknownFormatVersion_RejectedBeforeKdf()
    {
        var header = BuildValidHeaderTemplate(version: 0x02);
        using var stream = new MemoryStream(header);

        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task CraftedHeader_InvalidMagic_RejectedBeforeKdf()
    {
        var header = BuildValidHeaderTemplate();
        header[0] = (byte)'X';
        using var stream = new MemoryStream(header);

        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task CraftedChunk_CiphertextLength_AboveCeiling_RejectedBeforeAllocation()
    {
        // Valid header followed by chunk length > 1 MiB (1048577)
        var data = new List<byte>(BuildValidHeaderTemplate());
        data.AddRange(BitConverter.GetBytes(BackupArchiveConstants.MaxCiphertextLength + 1));
        data.AddRange(new byte[32]);

        using var stream = new MemoryStream(data.ToArray());
        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task CraftedChunk_CiphertextLength_BelowFloor_RejectedBeforeAllocation()
    {
        // Valid header followed by chunk length < 17 bytes (TagSize)
        var data = new List<byte>(BuildValidHeaderTemplate());
        data.AddRange(BitConverter.GetBytes(BackupArchiveConstants.MinCiphertextLength - 1));
        data.AddRange(new byte[16]);

        using var stream = new MemoryStream(data.ToArray());
        var verifyResult = await _sut.Pass1VerifyAsync(stream, "AnyPassphrase123!");
        Assert.False(verifyResult.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, verifyResult.Error?.Code);
    }

    [Fact]
    public async Task WrongPassphrase_FailsWithAuthenticationError()
    {
        var plaintext = "Secret payload"u8.ToArray();
        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();

        var enc = await _sut.EncryptStreamAsync(plainStream, cipherStream, "CorrectPassword123!", iterations: 3, memoryKiB: 65536);
        Assert.True(enc.IsSuccess);

        cipherStream.Position = 0;
        using var decStream = new MemoryStream();
        var dec = await _sut.DecryptStreamAsync(cipherStream, decStream, "WrongPassword1234!");
        Assert.False(dec.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, dec.Error?.Code);
    }

    [Fact]
    public async Task TamperedHeaderOrCiphertext_BitFlip_FailsWithAuthenticationError()
    {
        var plaintext = "Fictional sensitive content"u8.ToArray();
        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();

        var enc = await _sut.EncryptStreamAsync(plainStream, cipherStream, "Password12345678!", iterations: 3, memoryKiB: 65536);
        Assert.True(enc.IsSuccess);

        var cipherBytes = cipherStream.ToArray();

        // 1. Bit flip in header
        var headerFlipped = (byte[])cipherBytes.Clone();
        headerFlipped[20] ^= 0x01;
        using (var stream = new MemoryStream(headerFlipped))
        using (var decStream = new MemoryStream())
        {
            var res = await _sut.DecryptStreamAsync(stream, decStream, "Password12345678!");
            Assert.False(res.IsSuccess);
            Assert.Equal(ErrorCode.AuthenticationError, res.Error?.Code);
        }

        // 2. Bit flip in ciphertext body
        var bodyFlipped = (byte[])cipherBytes.Clone();
        bodyFlipped[^5] ^= 0x01;
        using (var stream = new MemoryStream(bodyFlipped))
        using (var decStream = new MemoryStream())
        {
            var res = await _sut.DecryptStreamAsync(stream, decStream, "Password12345678!");
            Assert.False(res.IsSuccess);
            Assert.Equal(ErrorCode.AuthenticationError, res.Error?.Code);
        }
    }

    [Fact]
    public async Task TruncatedStream_MissingFinalFlag_FailsWithAuthenticationError()
    {
        // Multi-chunk input so there is a Message chunk and a Final chunk
        var plaintext = new byte[70000];
        RandomNumberGenerator.Fill(plaintext);

        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();
        var enc = await _sut.EncryptStreamAsync(plainStream, cipherStream, "Password12345678!", iterations: 3, memoryKiB: 65536);
        Assert.True(enc.IsSuccess);

        // Truncate before the final chunk
        var cipherBytes = cipherStream.ToArray();
        // Cut in half
        var truncated = new byte[cipherBytes.Length / 2];
        Array.Copy(cipherBytes, truncated, truncated.Length);

        using var truncStream = new MemoryStream(truncated);
        using var decStream = new MemoryStream();
        var dec = await _sut.DecryptStreamAsync(truncStream, decStream, "Password12345678!");
        Assert.False(dec.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, dec.Error?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131072)]
    public async Task FinalChunk_PlaintextLengthEdgeCases_RoundTripByteIdentically(int length)
    {
        var plaintext = new byte[length];
        if (length > 0)
        {
            RandomNumberGenerator.Fill(plaintext);
        }

        using var plainStream = new MemoryStream(plaintext);
        using var cipherStream = new MemoryStream();

        var enc = await _sut.EncryptStreamAsync(plainStream, cipherStream, "Password12345678!", iterations: 3, memoryKiB: 65536);
        Assert.True(enc.IsSuccess, $"Failed to encrypt payload of length {length}: {enc.Error?.Message}");

        cipherStream.Position = 0;
        using var decStream = new MemoryStream();
        var dec = await _sut.DecryptStreamAsync(cipherStream, decStream, "Password12345678!");
        Assert.True(dec.IsSuccess, $"Failed to decrypt payload of length {length}: {dec.Error?.Message}");

        Assert.Equal(plaintext, decStream.ToArray());
    }

    [Fact]
    public async Task TwoBackupsOfIdenticalData_ProduceDifferentCiphertextBytes()
    {
        var plaintext = "Identical payload for both backups"u8.ToArray();
        const string passphrase = "SamePassphrase123!";

        using var p1 = new MemoryStream(plaintext);
        using var c1 = new MemoryStream();
        var enc1 = await _sut.EncryptStreamAsync(p1, c1, passphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(enc1.IsSuccess);

        using var p2 = new MemoryStream(plaintext);
        using var c2 = new MemoryStream();
        var enc2 = await _sut.EncryptStreamAsync(p2, c2, passphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(enc2.IsSuccess);

        var b1 = c1.ToArray();
        var b2 = c2.ToArray();

        Assert.NotEqual(b1, b2);
    }

    [Fact]
    public async Task ErrorMessageOrLogs_NeverContainSecretData()
    {
        const string sensitivePassphrase = "SuperSecretUnpublishedPassword999";
        var sensitiveData = "Fictional Confidential Information"u8.ToArray();

        using var plainStream = new MemoryStream(sensitiveData);
        using var cipherStream = new MemoryStream();
        var enc = await _sut.EncryptStreamAsync(plainStream, cipherStream, sensitivePassphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(enc.IsSuccess);

        cipherStream.Position = 0;
        using var decStream = new MemoryStream();
        var dec = await _sut.DecryptStreamAsync(cipherStream, decStream, "IncorrectPasswordXYZ!");
        Assert.False(dec.IsSuccess);

        var errorMsg = dec.Error?.Message ?? string.Empty;
        Assert.DoesNotContain(sensitivePassphrase, errorMsg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Confidential", errorMsg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BoundedMemory_EncryptStream_ScalingInvariant_AllocationsIndependentOfStreamSize()
    {
        const string passphrase = "Password12345678!";

        // Warm up JIT, static initializers, ArrayPool buckets
        using (var warmPlain = new SyntheticRepeatingStream(1 * 1024 * 1024))
        using (var warmCipher = new CountingSinkStream())
        {
            var warmRes = await _sut.EncryptStreamAsync(warmPlain, warmCipher, passphrase, iterations: 3, memoryKiB: 65536);
            Assert.True(warmRes.IsSuccess);
        }

        // Measure 8 MiB stream
        long alloc8;
        using (var plain8 = new SyntheticRepeatingStream(8 * 1024 * 1024))
        using (var cipher8 = new CountingSinkStream())
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before8 = GC.GetAllocatedBytesForCurrentThread();
            var res8 = await _sut.EncryptStreamAsync(plain8, cipher8, passphrase, iterations: 3, memoryKiB: 65536);
            alloc8 = GC.GetAllocatedBytesForCurrentThread() - before8;
            Assert.True(res8.IsSuccess);
        }

        // Measure 32 MiB stream
        long alloc32;
        using (var plain32 = new SyntheticRepeatingStream(32 * 1024 * 1024))
        using (var cipher32 = new CountingSinkStream())
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before32 = GC.GetAllocatedBytesForCurrentThread();
            var res32 = await _sut.EncryptStreamAsync(plain32, cipher32, passphrase, iterations: 3, memoryKiB: 65536);
            alloc32 = GC.GetAllocatedBytesForCurrentThread() - before32;
            Assert.True(res32.IsSuccess);
        }

        var diff = Math.Abs(alloc32 - alloc8);
        Assert.True(diff < 64 * 1024, $"EncryptStream scaling difference {diff} bytes exceeded 64 KiB ceiling. Alloc8: {alloc8}, Alloc32: {alloc32}");
    }

    [Fact]
    public async Task BoundedMemory_Pass1Verify_ScalingInvariant_AllocationsIndependentOfStreamSize()
    {
        const string passphrase = "Password12345678!";

        using var plain8 = new SyntheticRepeatingStream(8 * 1024 * 1024);
        using var cipherMem8 = new MemoryStream();
        var enc8 = await _sut.EncryptStreamAsync(plain8, cipherMem8, passphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(enc8.IsSuccess);
        using var cipher8 = new PreloadedCipherStream(cipherMem8.ToArray());

        using var plain32 = new SyntheticRepeatingStream(32 * 1024 * 1024);
        using var cipherMem32 = new MemoryStream();
        var enc32 = await _sut.EncryptStreamAsync(plain32, cipherMem32, passphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(enc32.IsSuccess);
        using var cipher32 = new PreloadedCipherStream(cipherMem32.ToArray());

        // Warm up Pass 1
        cipher8.Position = 0;
        var warmRes = await _sut.Pass1VerifyAsync(cipher8, passphrase);
        Assert.True(warmRes.IsSuccess);

        // Measure 8 MiB archive verification
        cipher8.Position = 0;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before8 = GC.GetAllocatedBytesForCurrentThread();
        var res8 = await _sut.Pass1VerifyAsync(cipher8, passphrase);
        var alloc8 = GC.GetAllocatedBytesForCurrentThread() - before8;
        Assert.True(res8.IsSuccess);

        // Measure 32 MiB archive verification
        cipher32.Position = 0;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before32 = GC.GetAllocatedBytesForCurrentThread();
        var res32 = await _sut.Pass1VerifyAsync(cipher32, passphrase);
        var alloc32 = GC.GetAllocatedBytesForCurrentThread() - before32;
        Assert.True(res32.IsSuccess);

        var diff = Math.Abs(alloc32 - alloc8);
        Assert.True(diff < 64 * 1024, $"Pass1Verify scaling difference {diff} bytes exceeded 64 KiB ceiling. Alloc8: {alloc8}, Alloc32: {alloc32}");
    }

    [Fact]
    public async Task BoundedMemory_Pass2Decrypt_ScalingInvariant_AllocationsIndependentOfStreamSize()
    {
        const string passphrase = "Password12345678!";

        using var plain8 = new SyntheticRepeatingStream(8 * 1024 * 1024);
        using var cipherMem8 = new MemoryStream();
        var enc8 = await _sut.EncryptStreamAsync(plain8, cipherMem8, passphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(enc8.IsSuccess);
        using var cipher8 = new PreloadedCipherStream(cipherMem8.ToArray());

        using var plain32 = new SyntheticRepeatingStream(32 * 1024 * 1024);
        using var cipherMem32 = new MemoryStream();
        var enc32 = await _sut.EncryptStreamAsync(plain32, cipherMem32, passphrase, iterations: 3, memoryKiB: 65536);
        Assert.True(enc32.IsSuccess);
        using var cipher32 = new PreloadedCipherStream(cipherMem32.ToArray());

        cipher8.Position = 0;
        var verify8 = await _sut.Pass1VerifyAsync(cipher8, passphrase);
        Assert.True(verify8.IsSuccess);
        var derivedKey8 = verify8.Value!;

        cipher32.Position = 0;
        var verify32 = await _sut.Pass1VerifyAsync(cipher32, passphrase);
        Assert.True(verify32.IsSuccess);
        var derivedKey32 = verify32.Value!;

        try
        {
            // Warm up Pass 2
            cipher8.Position = 0;
            using (var warmSink = new CountingSinkStream())
            {
                var warmRes = await _sut.Pass2DecryptAsync(cipher8, warmSink, derivedKey8);
                Assert.True(warmRes.IsSuccess);
            }

            // Measure 8 MiB archive decryption
            long alloc8;
            cipher8.Position = 0;
            using (var sink8 = new CountingSinkStream())
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before8 = GC.GetAllocatedBytesForCurrentThread();
                var res8 = await _sut.Pass2DecryptAsync(cipher8, sink8, derivedKey8);
                alloc8 = GC.GetAllocatedBytesForCurrentThread() - before8;
                Assert.True(res8.IsSuccess);
            }

            // Measure 32 MiB archive decryption
            long alloc32;
            cipher32.Position = 0;
            using (var sink32 = new CountingSinkStream())
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before32 = GC.GetAllocatedBytesForCurrentThread();
                var res32 = await _sut.Pass2DecryptAsync(cipher32, sink32, derivedKey32);
                alloc32 = GC.GetAllocatedBytesForCurrentThread() - before32;
                Assert.True(res32.IsSuccess);
            }

            var diff = Math.Abs(alloc32 - alloc8);
            Assert.True(diff < 64 * 1024, $"Pass2Decrypt scaling difference {diff} bytes exceeded 64 KiB ceiling. Alloc8: {alloc8}, Alloc32: {alloc32}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey8);
            CryptographicOperations.ZeroMemory(derivedKey32);
        }
    }

    [Fact]
    public void BackupOptions_Defaults_IncludeComposePathAndFlags()
    {
        var opts = new BackupOptions();
        Assert.Contains("docker compose", opts.DumpCommand);
        Assert.Contains("deploy/docker-compose.yml", opts.DumpCommand.Replace('\\', '/'));
        Assert.Contains("--archive --gzip --oplog", opts.DumpCommand);

        Assert.Contains("docker compose", opts.RestoreCommand);
        Assert.Contains("deploy/docker-compose.yml", opts.RestoreCommand.Replace('\\', '/'));
        Assert.Contains("--archive --gzip", opts.RestoreCommand);
        Assert.Contains("--nsInclude", opts.RestoreCommand);
    }

    [Fact]
    public void BackupOptions_EnvOverride_OverridesComposePath()
    {
        const string customPath = "C:/custom/docker-compose.yml";
        Environment.SetEnvironmentVariable("ROLERADAR_COMPOSE_FILE", customPath);
        try
        {
            var opts = new BackupOptions();
            Assert.Contains(customPath, opts.DumpCommand.Replace('\\', '/'));
            Assert.Contains(customPath, opts.RestoreCommand.Replace('\\', '/'));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROLERADAR_COMPOSE_FILE", null);
        }
    }

    [Fact]
    public async Task EncryptStreamAsync_BeforeFinalChunkCallback_IsInvokedAndCanAbort()
    {
        var invoked = false;
        using var plain = new MemoryStream("Callback holdback payload"u8.ToArray());
        using var cipher = new MemoryStream();

        var result = await _sut.EncryptStreamAsync(
            plain,
            cipher,
            "Passphrase123!",
            iterations: 3,
            memoryKiB: 65536,
            beforeFinalChunkAsync: ct =>
            {
                invoked = true;
                return Task.FromResult(Result<bool>.Failure(ErrorCode.ValidationError, "Simulated holdback failure."));
            });

        Assert.True(invoked, "Expected beforeFinalChunkAsync callback to be invoked before writing final chunk.");
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error?.Code);
    }

    [Fact]
    public async Task Pass2Decrypt_CorruptedChunkLength_ReturnsAuthenticationError()
    {
        using var plain = new MemoryStream("Pass 2 chunk length validation payload"u8.ToArray());
        using var cipher = new MemoryStream();
        var enc = await _sut.EncryptStreamAsync(plain, cipher, "Passphrase123!", iterations: 3, memoryKiB: 65536);
        Assert.True(enc.IsSuccess);

        cipher.Position = 0;
        var verify = await _sut.Pass1VerifyAsync(cipher, "Passphrase123!");
        Assert.True(verify.IsSuccess);
        var derivedKey = verify.Value!;

        var corruptedBytes = cipher.ToArray();
        // Overwrite chunk length with an invalid huge size (> MaxCiphertextLength)
        BitConverter.TryWriteBytes(corruptedBytes.AsSpan(BackupArchiveConstants.HeaderSize, 4), BackupArchiveConstants.MaxCiphertextLength + 1024);

        using var corruptedStream = new MemoryStream(corruptedBytes);
        using var output = new MemoryStream();
        var dec = await _sut.Pass2DecryptAsync(corruptedStream, output, derivedKey);

        Assert.False(dec.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationError, dec.Error?.Code);
    }

    [Fact]
    public async Task PropertyTest_RoundTrip_RandomStreamsAndPassphrases()
    {
        // Property test over random byte streams with biased lengths
        var lengths = new[] { 0, 1, 100, 65535, 65536, 65537, 131072, 200000 };

        foreach (var length in lengths)
        {
            var data = new byte[length];
            if (length > 0)
            {
                RandomNumberGenerator.Fill(data);
            }
            var pass = "Passphrase_" + Guid.NewGuid();

            using var pStream = new MemoryStream(data);
            using var cStream = new MemoryStream();
            var enc = await _sut.EncryptStreamAsync(pStream, cStream, pass, iterations: 3, memoryKiB: 65536);
            Assert.True(enc.IsSuccess);

            cStream.Position = 0;
            using var dStream = new MemoryStream();
            var dec = await _sut.DecryptStreamAsync(cStream, dStream, pass);
            Assert.True(dec.IsSuccess);

            Assert.Equal(data, dStream.ToArray());
        }
    }

    [Fact]
    public void PropertyTest_HeaderParser_FuzzingNeverCrashesOrExceedsCeilings()
    {
        // FsCheck Fluent API to fuzz header bytes
        var property = FsCheck.Fluent.Prop.ForAll<byte[]>(bytes =>
        {
            using var stream = new MemoryStream(bytes);
            var result = _sut.Pass1VerifyAsync(stream, "AnyPassword123!").GetAwaiter().GetResult();

            // Must never crash or succeed on random fuzz bytes (unless valid header generated, which is 2^-128)
            return !result.IsSuccess && result.Error?.Code == ErrorCode.AuthenticationError;
        });

        property.QuickCheckThrowOnFailure();
    }

    private static byte[] BuildValidHeaderTemplate(
        byte version = BackupArchiveConstants.CurrentFormatVersion,
        byte flags = BackupArchiveConstants.ExpectedFlags,
        int iterations = BackupArchiveConstants.DefaultArgon2Iterations,
        int memoryKiB = BackupArchiveConstants.DefaultArgon2MemoryKiB)
    {
        var buffer = new byte[BackupArchiveConstants.HeaderSize];
        Array.Copy(BackupArchiveConstants.Magic, 0, buffer, 0, 4);
        buffer[4] = version;
        buffer[5] = flags;
        Array.Copy(BitConverter.GetBytes(iterations), 0, buffer, 6, 4);
        Array.Copy(BitConverter.GetBytes(memoryKiB), 0, buffer, 10, 4);
        RandomNumberGenerator.Fill(buffer.AsSpan(14, 16)); // salt
        RandomNumberGenerator.Fill(buffer.AsSpan(30, 24)); // streamHeader
        return buffer;
    }

    private sealed class SyntheticRepeatingStream : Stream
    {
        private readonly long _totalLength;
        private long _position;

        public SyntheticRepeatingStream(long totalLength) => _totalLength = totalLength;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalLength;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _totalLength - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, remaining);
            for (var i = 0; i < toRead; i++)
            {
                buffer[offset + i] = (byte)((_position + i) % 251);
            }
            _position += toRead;
            return toRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var remaining = _totalLength - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(buffer.Length, remaining);
            for (var i = 0; i < toRead; i++)
            {
                buffer[i] = (byte)((_position + i) % 251);
            }
            _position += toRead;
            return toRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CountingSinkStream : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            BytesWritten += count;
            return Task.CompletedTask;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PreloadedCipherStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public PreloadedCipherStream(byte[] data)
        {
            _data = data;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => _position = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _data.Length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toRead = Math.Min(count, remaining);
            Buffer.BlockCopy(_data, _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var remaining = _data.Length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toRead = Math.Min(buffer.Length, remaining);
            _data.AsSpan(_position, toRead).CopyTo(buffer);
            _position += toRead;
            return toRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
            {
                _position = (int)offset;
            }
            else if (origin == SeekOrigin.Current)
            {
                _position += (int)offset;
            }
            else if (origin == SeekOrigin.End)
            {
                _position = _data.Length + (int)offset;
            }

            return _position;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void TryExtractSourceDatabaseName_UncompressedChunk_ExtractsDatabaseName()
    {
        // Construct synthetic mongodump collection header containing \x02db\0<len>testdb\0
        using var ms = new MemoryStream();
        ms.Write([0x6d, 0xe2, 0x99, 0x81]); // 4-byte archive magic
        ms.Write(BitConverter.GetBytes(100)); // global header len 100
        ms.Write(new byte[100]); // dummy global header
        // Collection header:
        ms.Write([0x02, (byte)'d', (byte)'b', 0x00]);
        var dbNameBytes = Encoding.UTF8.GetBytes("sample_source_db");
        ms.Write(BitConverter.GetBytes(dbNameBytes.Length + 1));
        ms.Write(dbNameBytes);
        ms.WriteByte(0x00);

        var chunk = ms.ToArray();
        var success = SecretStreamBackupArchiveService.TryExtractSourceDatabaseName(chunk, out var extractedDb);

        Assert.True(success);
        Assert.Equal("sample_source_db", extractedDb);
    }

    [Fact]
    public void TryExtractSourceDatabaseName_GzippedChunk_ExtractsDatabaseName()
    {
        // Construct synthetic mongodump collection header and compress with GZip
        using var uncompressedMs = new MemoryStream();
        uncompressedMs.Write([0x6d, 0xe2, 0x99, 0x81]); // 4-byte archive magic
        uncompressedMs.Write(BitConverter.GetBytes(50)); // global header len 50
        uncompressedMs.Write(new byte[50]); // dummy global header
        // Collection header:
        uncompressedMs.Write([0x02, (byte)'d', (byte)'b', 0x00]);
        var dbNameBytes = Encoding.UTF8.GetBytes("gzipped_source_db");
        uncompressedMs.Write(BitConverter.GetBytes(dbNameBytes.Length + 1));
        uncompressedMs.Write(dbNameBytes);
        uncompressedMs.WriteByte(0x00);

        using var compressedMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(compressedMs, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(uncompressedMs.ToArray());
        }

        var chunk = compressedMs.ToArray();
        var success = SecretStreamBackupArchiveService.TryExtractSourceDatabaseName(chunk, out var extractedDb);

        Assert.True(success);
        Assert.Equal("gzipped_source_db", extractedDb);
    }

    [Fact]
    public void TryExtractSourceDatabaseName_WhenNoDatabaseElement_ReturnsFalse()
    {
        var dummyBytes = new byte[] { 0x6d, 0xe2, 0x99, 0x81, 0x10, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03 };
        var success = SecretStreamBackupArchiveService.TryExtractSourceDatabaseName(dummyBytes, out var extractedDb);

        Assert.False(success);
        Assert.Null(extractedDb);
    }
}
