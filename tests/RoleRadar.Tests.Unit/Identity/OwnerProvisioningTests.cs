using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

public sealed class OwnerProvisioningTests
{
    [Fact]
    public void PasswordHasher_UsesPbkdf2_And_VerifiesCorrectPassword()
    {
        var hasher = new PasswordHasher<IdentityUser<Guid>>();
        var user = new IdentityUser<Guid> { UserName = "matt" };

        var hash = hasher.HashPassword(user, "SuperSecret123!");

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        // Default ASP.NET Core Identity password hasher uses PBKDF2 (V3 format starts with byte 0x01)
        var bytes = Convert.FromBase64String(hash);
        Assert.Equal(0x01, bytes[0]);

        var successResult = hasher.VerifyHashedPassword(user, hash, "SuperSecret123!");
        Assert.Equal(PasswordVerificationResult.Success, successResult);

        var failResult = hasher.VerifyHashedPassword(user, hash, "WrongPassword!");
        Assert.Equal(PasswordVerificationResult.Failed, failResult);
    }

    [Fact]
    public async Task ProvisionOwnerAsync_OnEmptyDatabase_Succeeds_And_SetsUpTotpKey()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var service = new OwnerProvisioningService(dbContext);

        // Act
        var result = await service.ProvisionOwnerAsync("matt", "InitialOwnerPassword123!");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("matt", result.Username);
        Assert.NotEmpty(result.AuthenticatorKey);
        Assert.StartsWith("otpauth://totp/RoleRadar:matt?secret=", result.OtpAuthUri);

        // Verify stored in DB with TwoFactorEnabled initially false until verified
        var user = await dbContext.Users.SingleAsync();
        Assert.Equal("matt", user.UserName);
        Assert.False(user.TwoFactorEnabled);
    }

    [Fact]
    public async Task ProvisionOwnerAsync_WhenOwnerAlreadyExists_ThrowsInvalidOperationException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var service = new OwnerProvisioningService(dbContext);
        await service.ProvisionOwnerAsync("matt", "InitialOwnerPassword123!");

        // Attempting to provision a second owner must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProvisionOwnerAsync("intruder", "SecondPassword456!"));
    }

    [Fact]
    public async Task VerifyTotpAndActivateAsync_WithValidCode_EnablesTwoFactor()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var service = new OwnerProvisioningService(dbContext);
        var provisionResult = await service.ProvisionOwnerAsync("matt", "InitialOwnerPassword123!");

        // Act: Generate valid current TOTP code using the shared secret
        var validCode = service.ComputeTotpCode(provisionResult.AuthenticatorKey);
        var activated = await service.VerifyTotpAndActivateAsync(provisionResult.UserId, validCode);

        // Assert
        Assert.True(activated);
        var user = await dbContext.Users.SingleAsync();
        Assert.True(user.TwoFactorEnabled);
    }

    [Fact]
    public async Task VerifyTotpAndActivateAsync_WithInvalidCode_Fails_And_LeavesTwoFactorDisabled()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var service = new OwnerProvisioningService(dbContext);
        var provisionResult = await service.ProvisionOwnerAsync("matt", "InitialOwnerPassword123!");

        // Act: attempt invalid code
        var activated = await service.VerifyTotpAndActivateAsync(provisionResult.UserId, "000000");

        // Assert
        Assert.False(activated);
        var user = await dbContext.Users.SingleAsync();
        Assert.False(user.TwoFactorEnabled);
    }
}
