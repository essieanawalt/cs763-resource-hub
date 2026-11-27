// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Entry point: --healthcheck probe and --migrate job modes, field key (secret file or value) and JWT setup (fail fast), deny-by-default authorization, rate limiting, request size limit, migration only over the owner connection, routes (~100%)
// Human Modifications: None yet
// Verification: Every WebApplicationFactory<Program> test in ResourceHub.Tests; HealthProbeTests, MigrationJobTests, NoOwnerConnectionTests; ComposeStackTests against docker compose
// Confidence: High

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Data;
using ResourceHub.Api.Endpoints;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

// Container health check: no host, no configuration, just GET /api/v1/health on loopback.
if (args.Contains(HealthProbe.Argument))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"));
}

// One-shot migration job, run as the schema owner in its own container (never in the API's).
if (args.Contains(MigrationJob.Argument))
{
    var jobConfig = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    return await MigrationJob.RunAsync(jobConfig, Console.Out, CancellationToken.None);
}

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HubDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'ConnectionStrings:HubDb' is not configured.");
}

// Refuse to start without a valid field key, issuer, audience and JWKS file.
var encryptor = FieldEncryptor.FromConfiguration(builder.Configuration);

// Only set where this process may change the schema (tests, local runs); never in the API container.
var ownerConnectionString = builder.Configuration.GetConnectionString(DatabaseStartup.OwnerConnectionName);

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
// Deny by default: every endpoint needs a valid token unless it opts out (only health does).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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

// With an owner connection, create or upgrade the schema (and the static mock data) as the owner
// and grant the runtime role its data access, before serving requests. Without one (Compose), the
// migration job has already done this. If the database is down, keep running so /api/v1/health
// can report 503.
if (string.IsNullOrWhiteSpace(ownerConnectionString))
{
    app.Logger.LogInformation("No owner connection configured; schema migrations are left to the migration job (--migrate).");
}
else
{
    try
    {
        var runtimeRole = DatabaseStartup.RuntimeRoleFrom(builder.Configuration)
            ?? throw new InvalidOperationException($"{DatabaseStartup.RuntimeRoleKey} is not set and ConnectionStrings:HubDb names no user.");
        await DatabaseStartup.MigrateAsOwnerAsync(ownerConnectionString, runtimeRole, encryptor, CancellationToken.None);
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
return 0;
