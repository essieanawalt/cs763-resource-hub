// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the startup migration service (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using Microsoft.EntityFrameworkCore;

namespace ResourceHub.Api.Data;

// Applies EF Core migrations (schema and seed data) in the background, retrying until the
// database is reachable. The app keeps serving meanwhile, so /api/v1/health still answers
// 503 instead of the process crashing when PostgreSQL is down.
// Turn off with Database:MigrateOnStartup=false.
public sealed class DatabaseMigrator : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<DatabaseMigrator> logger;

    public DatabaseMigrator(IServiceScopeFactory scopes, ILogger<DatabaseMigrator> logger)
    {
        this.scopes = scopes;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
                await db.Database.MigrateAsync(stoppingToken);
                logger.LogInformation("Database migrations applied");
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Database migration failed; retrying in {Delay}", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
