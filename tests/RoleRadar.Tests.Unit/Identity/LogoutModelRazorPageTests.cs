using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Pages.Account;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

public sealed class LogoutModelRazorPageTests
{
    private static ServiceProvider CreateTestServiceProvider(IdentityAppDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(b => b.AddConsole());
        services.AddAuthentication();
        services.AddHttpContextAccessor();

        services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityAppDbContext>()
            .AddDefaultTokenProviders();

        services.AddIdentityServer(options =>
        {
            options.Authentication.CookieAuthenticationScheme = IdentityConstants.ApplicationScheme;
        })
        .AddAspNetIdentity<IdentityUser<Guid>>();

        services.AddScoped(_ => dbContext);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task OnGetAsync_WhenNoLogoutContext_DoesNotSignOutAndReturnsPageWithoutRedirect()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        using var provider = CreateTestServiceProvider(dbContext);
        var signInManager = provider.GetRequiredService<SignInManager<IdentityUser<Guid>>>();
        var interactionService = provider.GetRequiredService<IIdentityServerInteractionService>();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = httpContext;
        signInManager.Context = httpContext;

        var logoutModel = new LogoutModel(signInManager, interactionService)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext
            }
        };

        var result = await logoutModel.OnGetAsync(null);

        Assert.IsType<PageResult>(result);
        Assert.Null(logoutModel.LogoutId);
    }

    [Fact]
    public async Task OnPostAsync_WhenNoLogoutContext_SignsOutAndReturnsPageWithoutRedirect()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        using var provider = CreateTestServiceProvider(dbContext);
        var signInManager = provider.GetRequiredService<SignInManager<IdentityUser<Guid>>>();
        var interactionService = provider.GetRequiredService<IIdentityServerInteractionService>();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = httpContext;
        signInManager.Context = httpContext;

        var logoutModel = new LogoutModel(signInManager, interactionService)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext
            }
        };

        var result = await logoutModel.OnPostAsync(null);

        Assert.IsType<PageResult>(result);
        Assert.Null(logoutModel.LogoutId);
    }
}
