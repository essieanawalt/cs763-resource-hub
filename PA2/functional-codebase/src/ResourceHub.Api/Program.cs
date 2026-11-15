// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Application entry point: services, JSON options, startup migration, routes (~100%)
// Human Modifications: None yet
// Verification: API integration tests in ResourceHub.Tests; docker compose up walkthrough
// Confidence: High

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Data;
using ResourceHub.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HubDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'ConnectionStrings:HubDb' is not configured.");
}

builder.Services.AddDbContext<HubDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ResourceHub.Api.Services.AllocationService>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Create or upgrade the schema (and the static mock data) before serving requests.
// If the database is down, keep running so /api/v1/health can report 503.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException)
    {
        app.Logger.LogError(ex, "Database migration failed at startup; the service will report unhealthy until the database is reachable.");
    }
}

app.MapHealthEndpoints();
app.MapAssetEndpoints();
app.MapTicketEndpoints();
app.MapAllocationEndpoints();

await app.RunAsync();

public partial class Program;
