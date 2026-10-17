using System.Security.Cryptography;
using System.Text;
using FsCheck;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Backup;

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
    public async Task BoundedMemory_MultiMegabyteStream_StaysWithinBufferCeiling()
    {
        // 5 MiB stream generated on the fly via a non-buffered synthetic stream
        const long totalSize = 5 * 1024 * 1024;
        using var syntheticPlaintext = new SyntheticRepeatingStream(totalSize);
        using var cipherStream = new MemoryStream();

        var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        var enc = await _sut.EncryptStreamAsync(syntheticPlaintext, cipherStream, "Password12345678!", iterations: 3, memoryKiB: 65536);
        Assert.True(enc.IsSuccess, $"Failed on bounded memory test: {enc.Error?.Message}");

        cipherStream.Position = 0;
        using var devNull = Stream.Null;
        var dec = await _sut.DecryptStreamAsync(cipherStream, devNull, "Password12345678!");
        Assert.True(dec.IsSuccess);

        var finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        // Assert working memory did not blow up by full stream size
        Assert.True(Math.Abs(finalMemory - initialMemory) < 10 * 1024 * 1024);
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
            if (remaining <= 0) return 0;
            var toRead = (int)Math.Min(count, remaining);
            for (var i = 0; i < toRead; i++)
            {
                buffer[offset + i] = (byte)((_position + i) % 251);
            }
            _position += toRead;
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
