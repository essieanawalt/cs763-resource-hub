// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: PA1 startup (~95%); PA2 added services, X-User-Id auth, JSON options, ProblemDetails and migrations (~100%)
// Human Modifications: PA1 version had a small human refactor (~5%)
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Data;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Resolved when the DbContext is created so tests can override configuration.
builder.Services.AddDbContext<HubDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("HubDb")
        ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:HubDb' is not configured.");
    options.UseNpgsql(connectionString);
});

// No default scheme: only endpoints marked [Authorize] read X-User-Id, so /api/v1/health stays
// open and never touches the database for authentication.
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, UserHeaderAuthenticationHandler>(UserHeaderAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(UserHeaderAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<AllocationService>();

if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    builder.Services.AddHostedService<DatabaseMigrator>();
}

builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Enums as snake_case names ("course_instructor", "checkedout"); numbers are refused.
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Unhandled errors and bare 401/403 responses come back as ProblemDetails, without stack traces.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

// Lets WebApplicationFactory<Program> find the entry point.
public partial class Program;
