// AI Contribution Weight: [95%] AI Generated / [5%] Human Refactored
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Resolved when the DbContext is created so tests can override configuration.
builder.Services.AddDbContext<HubDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("HubDb")
        ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:HubDb' is not configured.");
    options.UseNpgsql(connectionString);
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
