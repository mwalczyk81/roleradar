var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();

app.Run();

namespace RoleRadar.Api
{
    public partial class Program;
}
