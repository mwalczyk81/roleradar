using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RoleRadar.Identity.Data;

public class IdentityAppDbContextFactory : IDesignTimeDbContextFactory<IdentityAppDbContext>
{
    public IdentityAppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityAppDbContext>();
        optionsBuilder.UseSqlite("Data Source=identity.db");

        return new IdentityAppDbContext(optionsBuilder.Options);
    }
}
