namespace RoleRadar.Infrastructure.Backup;

public static class BackupArchiveConstants
{
    public static readonly byte[] Magic = "RRBK"u8.ToArray();
    public const byte CurrentFormatVersion = 0x01;
    public const byte ExpectedFlags = 0x00;

    public const int DefaultArgon2Iterations = 4;
    public const int MinArgon2Iterations = 3;
    public const int MaxArgon2Iterations = 10;

    public const int DefaultArgon2MemoryKiB = 262144; // 256 MiB
    public const int MinArgon2MemoryKiB = 65536; // 64 MiB
    public const int MaxArgon2MemoryKiB = 1048576; // 1 GiB

    public const int KeySize = 32;
    public const int SaltSize = 16;
    public const int StreamHeaderSize = 24;
    public const int HeaderSize = 54; // 4 + 1 + 1 + 4 + 4 + 16 + 24

    public const int ChunkPlaintextSize = 65536; // 64 KiB
    public const int TagSize = 17;
    public const int MinCiphertextLength = 17; // TagSize (0-byte payload)
    public const int MaxCiphertextLength = 1048576; // 1 MiB

    public const int MaxAllowedBufferAllocation = 262144; // 256 KiB
}
