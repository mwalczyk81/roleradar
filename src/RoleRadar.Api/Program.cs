using RoleRadar.Api.Authentication;
using RoleRadar.Application;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<OwnerSecurityOptions>(builder.Configuration.GetSection(OwnerSecurityOptions.SectionName));
builder.Services.AddScoped<IOwnerIdProvider, HttpUserOwnerIdProvider>();

builder.Services.AddRoleRadarApplication();
builder.Services.AddRoleRadarInfrastructure(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoConnection")
        ?? RoleRadar.Infrastructure.Mongo.MongoConnectionStringSource.Resolve(Environment.GetEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.ConnectionString = connectionString.Trim();
    }

    var databaseName = builder.Configuration["DatabaseName"]
        ?? Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");
    if (!string.IsNullOrWhiteSpace(databaseName))
    {
        options.DatabaseName = databaseName.Trim();
    }
});

builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();

app.Run();

namespace RoleRadar.Api
{
    public partial class Program;
}
