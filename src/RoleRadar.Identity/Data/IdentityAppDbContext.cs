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
    }
}
