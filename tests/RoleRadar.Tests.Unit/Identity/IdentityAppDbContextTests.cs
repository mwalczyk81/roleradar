using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Identity.Data;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

public sealed class IdentityAppDbContextTests
{
    [Fact]
    public void IdentityAppDbContext_CanApplyMigrations_OnSqliteDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new IdentityAppDbContext(options))
        {
            context.Database.Migrate();

            // Verify operational tables can be queried
            Assert.Empty(context.Users);
            Assert.Empty(context.Keys);
            Assert.Empty(context.PersistedGrants);
            Assert.Empty(context.Clients);
        }
    }
}
