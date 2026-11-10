using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Pages.Account;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

public sealed class LoginModelRazorPageTests
{
    [Fact]
    public async Task OnPostAsync_WhenPasswordStepRequiresTwoFactor_ClearsStaleModelState()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddLogging(b => b.AddConsole());
        services.AddAuthentication();
        services.AddHttpContextAccessor();

        services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(opt =>
        {
            opt.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<IdentityAppDbContext>()
        .AddDefaultTokenProviders();

        services.AddIdentityServer(options =>
        {
            options.Authentication.CookieAuthenticationScheme = IdentityConstants.ApplicationScheme;
        })
        .AddAspNetIdentity<IdentityUser<Guid>>();

        services.AddScoped(_ => dbContext);

        using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var signInManager = provider.GetRequiredService<SignInManager<IdentityUser<Guid>>>();
        var interactionService = provider.GetRequiredService<IIdentityServerInteractionService>();

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "matt",
            TwoFactorEnabled = true,
            LockoutEnabled = true,
        };

        var createResult = await userManager.CreateAsync(user, "Password123!");
        Assert.True(createResult.Succeeded);

        // Register 2FA authenticator key token so Identity requires two factor
        dbContext.UserTokens.Add(new IdentityUserToken<Guid>
        {
            UserId = user.Id,
            LoginProvider = "[AspNetUserStore]",
            Name = "AuthenticatorKey",
            Value = "MOCKAUTHENTICATORKEY123"
        });
        await dbContext.SaveChangesAsync();

        // Configure HttpContext for SignInManager
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = httpContext;
        signInManager.Context = httpContext;

        var loginModel = new LoginModel(
            signInManager,
            userManager,
            OwnerProvisioningService.Create(dbContext),
            interactionService,
            NullLogger<LoginModel>.Instance)
        {
            Input = new LoginModel.InputModel
            {
                Username = "matt",
                Password = "Password123!"
            }
        };

        // Simulate Razor Pages model binding populating TwoFactorInput.Code validation failure
        loginModel.ModelState.AddModelError("TwoFactorInput.Code", "The Code field is required.");
        Assert.False(loginModel.ModelState.IsValid, "Precondition: ModelState must initially have stale validation error.");

        var result = await loginModel.OnPostAsync();

        // Must move to OTP step
        Assert.True(loginModel.RequiresTwoFactor, "Moving to OTP step must set RequiresTwoFactor to true.");

        // Stale model error must be cleared so 'The Code field is required' does not display above OTP field
        Assert.True(loginModel.ModelState.IsValid, "ModelState must be cleared when moving to OTP step.");
        Assert.Empty(loginModel.ModelState);
    }
}
