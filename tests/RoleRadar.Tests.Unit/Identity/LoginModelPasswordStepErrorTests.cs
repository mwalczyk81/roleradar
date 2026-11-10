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

/// <summary>
/// Feature 010, user story 4. Razor Pages binds and validates both <c>Input</c> and <c>TwoFactorInput</c> on every post, so
/// on the password step the empty one-time-code field already sits in ModelState ("The Code field is required" and the
/// 6-to-7 length message). The page lists every ModelState error, so a wrong password used to show those under
/// "Invalid username or password.". Only the error of the step the person is on may show.
/// The real binder records them under the key "Code" (no prefix): the password step posts no TwoFactorInput field, so
/// MVC falls back to the empty prefix. On the code step the field is posted, so the key is "TwoFactorInput.Code".
/// </summary>
public sealed class LoginModelPasswordStepErrorTests
{
    private const string CodeRequired = "The Code field is required.";

    private const string CodeLength = "The field Code must be a string with a minimum length of 6 and a maximum length of 7.";

    private sealed class Harness(SqliteConnection connection, IdentityAppDbContext dbContext, ServiceProvider provider, LoginModel model) : IAsyncDisposable
    {
        public LoginModel Model { get; } = model;

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await dbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateAsync(string username, string password, bool requiresTwoFactor, string? code = null, string codeKey = "Code")
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>().UseSqlite(connection).Options;
        var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddLogging(b => b.AddConsole());
        services.AddAuthentication();
        services.AddHttpContextAccessor();
        services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(opt => opt.Lockout.AllowedForNewUsers = true)
            .AddEntityFrameworkStores<IdentityAppDbContext>()
            .AddDefaultTokenProviders();
        services.AddIdentityServer(o => o.Authentication.CookieAuthenticationScheme = IdentityConstants.ApplicationScheme)
            .AddAspNetIdentity<IdentityUser<Guid>>();
        services.AddScoped(_ => dbContext);

        var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var signInManager = provider.GetRequiredService<SignInManager<IdentityUser<Guid>>>();

        // A fictional owner without two-factor, so a wrong password is a plain failed sign-in.
        var user = new IdentityUser<Guid> { Id = Guid.CreateVersion7(), UserName = "demo-owner", LockoutEnabled = true };
        Assert.True((await userManager.CreateAsync(user, "Correct-Horse-1!")).Succeeded);

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;
        signInManager.Context = httpContext;

        var model = new LoginModel(signInManager, userManager, OwnerProvisioningService.Create(dbContext), provider.GetRequiredService<IIdentityServerInteractionService>(), NullLogger<LoginModel>.Instance)
        {
            Input = new LoginModel.InputModel { Username = username, Password = password },
            RequiresTwoFactor = requiresTwoFactor,
            TwoFactorInput = new LoginModel.TwoFactorModel { Code = code ?? string.Empty }
        };

        // What model binding leaves behind for the code field the form did not post (key "Code" on the password step).
        model.ModelState.AddModelError(codeKey, CodeRequired);
        model.ModelState.AddModelError(codeKey, CodeLength);

        return new Harness(connection, dbContext, provider, model);
    }

    private static string[] Messages(LoginModel model) =>
        [.. model.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)];

    [Fact]
    public async Task OnPostAsync_WrongPassword_ShowsOnlyThePasswordStepError()
    {
        await using var harness = await CreateAsync("demo-owner", "not-the-password", requiresTwoFactor: false);
        Assert.Equal([CodeRequired, CodeLength], Messages(harness.Model));

        await harness.Model.OnPostAsync();

        Assert.Equal(["Invalid username or password."], Messages(harness.Model));
        Assert.DoesNotContain(harness.Model.ModelState.Keys, k => k == "Code" || k.StartsWith("TwoFactorInput", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAsync_WrongPassword_AlsoDropsPrefixedCodeErrors()
    {
        await using var harness = await CreateAsync("demo-owner", "not-the-password", requiresTwoFactor: false, codeKey: "TwoFactorInput.Code");

        await harness.Model.OnPostAsync();

        Assert.Equal(["Invalid username or password."], Messages(harness.Model));
    }

    [Fact]
    public async Task OnPostAsync_UnknownUser_ShowsOnlyThePasswordStepError()
    {
        await using var harness = await CreateAsync("someone-else", "whatever", requiresTwoFactor: false);

        await harness.Model.OnPostAsync();

        Assert.Equal(["Invalid username or password."], Messages(harness.Model));
    }

    [Fact]
    public async Task OnPostAsync_BlankUsernameOrPassword_ShowsOnlyTheRequiredMessageOfThePasswordStep()
    {
        await using var harness = await CreateAsync(string.Empty, string.Empty, requiresTwoFactor: false);

        await harness.Model.OnPostAsync();

        Assert.Equal(["Username and password are required."], Messages(harness.Model));
    }

    [Fact]
    public async Task OnPostAsync_OnTheCodeStep_StillShowsTheCodeFieldMessages()
    {
        // The one-time-code step is unchanged: its own field messages stay, with the step's own message.
        await using var harness = await CreateAsync("demo-owner", string.Empty, requiresTwoFactor: true, code: string.Empty, codeKey: "TwoFactorInput.Code");

        await harness.Model.OnPostAsync();

        var messages = Messages(harness.Model);
        Assert.Contains(CodeRequired, messages);
        Assert.Contains(CodeLength, messages);
        Assert.Contains("Please enter your 6-digit authenticator code.", messages);
    }
}
