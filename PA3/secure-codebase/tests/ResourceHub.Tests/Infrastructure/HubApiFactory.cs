// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Test host with a fresh database per test class and a controllable clock (~100%)
// Human Modifications: None (review only)
// Verification: Used by every database test in ResourceHub.Tests; reviewed by me 2026-11-15
// Confidence: High

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace ResourceHub.Tests.Infrastructure;

/// <summary>A clock the tests can move forward (for due dates and overdue devices).</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>
/// Runs the API in memory against its own PostgreSQL database, created (and migrated and
/// seeded) at startup and dropped when the test class finishes.
/// </summary>
public class HubApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"rh_test_{Guid.NewGuid():N}";

    public TestClock Clock { get; } = new();

    protected virtual string ConnectionString => TestDb.ForDatabase(_databaseName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:HubDb", ConnectionString);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public virtual Task InitializeAsync()
    {
        // Starting the host applies the migrations and seed data.
        _ = Server;
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (TestDb.BaseConnectionString is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestDb.BaseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // The name is generated above from a GUID, never from input.
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Points the API at a database server that does not exist.</summary>
public sealed class UnreachableDbFactory : HubApiFactory
{
    protected override string ConnectionString =>
        "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=none;Timeout=2";
}
