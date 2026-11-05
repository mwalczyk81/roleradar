using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

public enum ApplicationAppendFault
{
    None,
    ThrowAfterPostingWrite,
    ReturnFailedResult,
}

internal sealed class ApplicationFaultSwitch
{
    public ApplicationAppendFault Mode { get; set; }
}

/// <summary>
/// In-process RoleRadar.Api against an isolated Testcontainers Mongo database, with an optional fault
/// switch on <see cref="IApplicationRepository.AppendEventAsync"/>. Everything else is the real
/// composition: real decorator, real transaction, real repositories. Fictional data only.
/// </summary>
internal sealed class PostingRejectionSyncHost : IAsyncDisposable
{
    private const string ValidIssuer = "https://identity.roleradar.local";
    private const string ValidAudience = "roleradar.api";
    private const string AllowedSubject = "0192e4a1-0000-7000-8000-000000000001";

    private readonly RSA _rsa;
    private readonly WebApplication _app;
    private readonly IMongoClient _rawClient;

    private PostingRejectionSyncHost(RSA rsa, WebApplication app, HttpClient client, string databaseName, IMongoClient rawClient, ApplicationFaultSwitch faults)
    {
        _rsa = rsa;
        _app = app;
        Client = client;
        DatabaseName = databaseName;
        _rawClient = rawClient;
        Faults = faults;
    }

    public HttpClient Client { get; }

    public string DatabaseName { get; }

    public ApplicationFaultSwitch Faults { get; }

    public IServiceProvider Services => _app.Services;

    public IMongoDatabase Database => _rawClient.GetDatabase(DatabaseName);

    public static async Task<PostingRejectionSyncHost> StartAsync(SharedMongoFixture mongo, string dbPrefix)
    {
        var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "rejection-sync-rsa-1" };
        var dbName = mongo.CreateDatabaseName(dbPrefix);
        var faults = new ApplicationFaultSwitch();

        var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = mongo.ConnectionString,
                    ["DatabaseName"] = dbName,
                    ["Authentication:Authority"] = ValidIssuer,
                    ["Authentication:Audience"] = ValidAudience,
                    ["Owner:AllowedSubjects:0"] = AllowedSubject
                });

                builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.RequireHttpsMetadata = false;
                    var oidcConfig = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = ValidIssuer };
                    oidcConfig.SigningKeys.Add(rsaKey);
                    options.Configuration = oidcConfig;
                    options.TokenValidationParameters.ValidIssuer = ValidIssuer;
                    options.TokenValidationParameters.ValidAudience = ValidAudience;
                    options.TokenValidationParameters.IssuerSigningKeys = [rsaKey];
                });

                // Same construction as the infrastructure registration, wrapped with the fault switch. The
                // wrapper shares the DI IMongoClient so it joins the ambient transaction session.
                builder.Services.AddScoped<IApplicationRepository>(sp =>
                    new FaultyApplicationRepository(
                        new MongoApplicationRepository(sp.GetRequiredService<IMongoClient>(), sp.GetRequiredService<IMongoClient>().GetDatabase(dbName)),
                        faults));
            });

        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        var client = new HttpClient { BaseAddress = new Uri(address) };
        var token = new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            Subject = new ClaimsIdentity([new Claim("sub", AllowedSubject), new Claim("scope", "roleradar.api")]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return new PostingRejectionSyncHost(rsa, app, client, dbName, new MongoClient(mongo.ConnectionString), faults);
    }

    public ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto> TransitionHandler(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto>>();

    public async Task<SeededPosting> SeedAppliedPostingAsync(string title = "Staff Platform Engineer")
    {
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;
        var company = await sp.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>()
            .HandleAsync(new RecordCompanyCommand(Name: $"Harbor Analytics {Guid.NewGuid():N}"));
        Assert.True(company.IsSuccess, company.Error?.Message);

        var posting = await sp.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>()
            .HandleAsync(new RecordPostingCommand(
                CompanyId: company.Value!.Company.Id,
                Title: title,
                RequisitionId: $"HAR-{Guid.NewGuid():N}"[..12],
                AtsTenant: "harbor"));
        Assert.True(posting.IsSuccess, posting.Error?.Message);
        var postingId = posting.Value!.Posting!.Id;

        var application = await sp.GetRequiredService<ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>>()
            .HandleAsync(new RecordApplicationCommand(
                PostingId: postingId,
                Action: "create",
                SubmittedDate: "2026-09-01",
                ResumeVariant: "tailored"));
        Assert.True(application.IsSuccess, application.Error?.Message);

        var stored = await sp.GetRequiredService<IPostingRepository>().GetByIdAsync(RoleRadar.Domain.Common.OwnerId.Default.Value, postingId);
        Assert.Equal(PostingDisposition.Applied, stored.Value!.Disposition);
        return new SeededPosting(postingId, application.Value!.ApplicationId, stored.Value.VersionToken);
    }

    public async Task<BsonDocument> PostingDocAsync(Guid postingId) =>
        await Database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(postingId, GuidRepresentation.Standard)))
            .SingleAsync();

    public async Task<BsonDocument> ApplicationDocAsync(Guid applicationId) =>
        await Database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard)))
            .SingleAsync();

    public async Task<List<BsonDocument>> EventDocsAsync(Guid applicationId) =>
        await Database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("application_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard)))
            .ToListAsync();

    public async Task<List<BsonDocument>> AuditDocsAsync() =>
        await Database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync();

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _rawClient.DropDatabaseAsync(DatabaseName);
        }
        finally
        {
            _rsa.Dispose();
        }
    }
}

internal sealed record SeededPosting(Guid PostingId, Guid ApplicationId, Guid VersionToken);

internal sealed class FaultyApplicationRepository(IApplicationRepository inner, ApplicationFaultSwitch faults) : IApplicationRepository
{
    public Task<Result<RoleRadar.Domain.Tracking.Application?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(ownerId, id, cancellationToken);

    public Task<Result<RoleRadar.Domain.Tracking.Application?>> GetByPostingIdAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default) =>
        inner.GetByPostingIdAsync(ownerId, postingId, cancellationToken);

    public Task<Result<bool>> UpsertApplicationAsync(RoleRadar.Domain.Tracking.Application application, CancellationToken cancellationToken = default) =>
        inner.UpsertApplicationAsync(application, cancellationToken);

    public Task<Result<bool>> AppendEventAsync(ApplicationEvent appEvent, CancellationToken cancellationToken = default) =>
        faults.Mode switch
        {
            ApplicationAppendFault.ThrowAfterPostingWrite => throw new InvalidOperationException("Injected failure after the posting write."),
            ApplicationAppendFault.ReturnFailedResult => Task.FromResult(Result<bool>.Failure(ErrorCode.DatastoreOffline, "Injected failed result.")),
            _ => inner.AppendEventAsync(appEvent, cancellationToken),
        };

    public Task<Result<IReadOnlyList<ApplicationEvent>>> GetEventsAsync(string ownerId, Guid applicationId, CancellationToken cancellationToken = default) =>
        inner.GetEventsAsync(ownerId, applicationId, cancellationToken);

    public Task<Result<IReadOnlyList<RoleRadar.Domain.Tracking.Application>>> ListApplicationsAsync(string ownerId, string? outcome = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
        inner.ListApplicationsAsync(ownerId, outcome, page, pageSize, cancellationToken);

    public Task<Result<int>> CountApplicationsAsync(string ownerId, string? outcome = null, CancellationToken cancellationToken = default) =>
        inner.CountApplicationsAsync(ownerId, outcome, cancellationToken);
}
