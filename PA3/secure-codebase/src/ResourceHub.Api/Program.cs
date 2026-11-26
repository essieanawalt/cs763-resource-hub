// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Entry point: field key and JWT setup (fail fast), deny-by-default authorization, rate limiting, request size limit, startup migration, routes (~100%)
// Human Modifications: None yet
// Verification: Every WebApplicationFactory<Program> test in ResourceHub.Tests; ComposeStackTests against docker compose
// Confidence: High

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Data;
using ResourceHub.Api.Endpoints;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HubDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'ConnectionStrings:HubDb' is not configured.");
}

// Refuse to start without a valid field key, issuer, audience and JWKS file.
var encryptor = FieldEncryptor.FromBase64(builder.Configuration[FieldEncryptor.ConfigKey]);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = 2 * 1024 * 1024;   // T3: the largest valid body (10 000 keys) fits
});

builder.Services.AddDbContext<HubDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(encryptor);
builder.Services.AddScoped<AllocationService>();
builder.Services.AddScoped<AuditTrail>();
builder.Services.AddProblemDetails();
builder.Services.AddLocalJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    // Deny by default: every endpoint needs a valid token unless it opts out (only health does).
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddHubRateLimiting(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Create or upgrade the schema (and the static mock data) before serving requests.
// If the database is down, keep running so /api/v1/health can report 503.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    try
    {
        await DatabaseStartup.MigrateAsync(db, encryptor, CancellationToken.None);
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
