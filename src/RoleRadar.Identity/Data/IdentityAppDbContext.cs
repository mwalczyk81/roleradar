using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Extensions;
using Duende.IdentityServer.EntityFramework.Interfaces;
using Duende.IdentityServer.EntityFramework.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RoleRadar.Identity.Data;

public class IdentityAppDbContext : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>,
    IPersistedGrantDbContext,
    IConfigurationDbContext
{
    private readonly OperationalStoreOptions? _operationalOptions;
    private readonly ConfigurationStoreOptions? _configurationOptions;

    public IdentityAppDbContext(
        DbContextOptions<IdentityAppDbContext> options,
        OperationalStoreOptions? operationalOptions = null,
        ConfigurationStoreOptions? configurationOptions = null)
        : base(options)
    {
        _operationalOptions = operationalOptions;
        _configurationOptions = configurationOptions;
    }

    /// <summary>Feature 014 (ADR 0042): codes already accepted, so a reused one-time code is rejected.</summary>
    public DbSet<UsedAuthenticatorCode> UsedAuthenticatorCodes { get; set; } = null!;

    // IPersistedGrantDbContext
    public DbSet<PersistedGrant> PersistedGrants { get; set; } = null!;
    public DbSet<DeviceFlowCodes> DeviceFlowCodes { get; set; } = null!;
    public DbSet<Key> Keys { get; set; } = null!;
    public DbSet<ServerSideSession> ServerSideSessions { get; set; } = null!;
    public DbSet<PushedAuthorizationRequest> PushedAuthorizationRequests { get; set; } = null!;
    public DbSet<SamlSigninState> SamlSigninStates { get; set; } = null!;
    public DbSet<SamlLogoutSession> SamlLogoutSessions { get; set; } = null!;
    public DbSet<SamlLogoutSessionRequestIndex> SamlLogoutSessionRequestIndices { get; set; } = null!;

    // IConfigurationDbContext
    public DbSet<Client> Clients { get; set; } = null!;
    public DbSet<ClientCorsOrigin> ClientCorsOrigins { get; set; } = null!;
    public DbSet<IdentityResource> IdentityResources { get; set; } = null!;
    public DbSet<ApiResource> ApiResources { get; set; } = null!;
    public DbSet<ApiScope> ApiScopes { get; set; } = null!;
    public DbSet<IdentityProvider> IdentityProviders { get; set; } = null!;
    public DbSet<SamlServiceProvider> SamlServiceProviders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var operationalOptions = _operationalOptions ?? new OperationalStoreOptions();
        builder.ConfigurePersistedGrantContext(operationalOptions);

        var configurationOptions = _configurationOptions ?? new ConfigurationStoreOptions();
        builder.ConfigureClientContext(configurationOptions);
        builder.ConfigureResourcesContext(configurationOptions);
        builder.ConfigureIdentityProviderContext(configurationOptions);

        builder.Entity<UsedAuthenticatorCode>(entity =>
        {
            entity.ToTable("UsedAuthenticatorCodes");

            // The primary key is the unique constraint that makes two simultaneous attempts safe: one insert wins.
            entity.HasKey(code => new { code.UserId, code.CodeHash });
            entity.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(code => code.OwnerId).IsRequired();

            // UTC ticks as a number, so "expires before now" translates and orders correctly on SQLite.
            entity.Property(code => code.AcceptedAtUtc).HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero));
            entity.Property(code => code.ExpiresAtUtc).HasConversion(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero));
            entity.HasIndex(code => code.ExpiresAtUtc);

            entity.HasOne<IdentityUser<Guid>>()
                .WithMany()
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
