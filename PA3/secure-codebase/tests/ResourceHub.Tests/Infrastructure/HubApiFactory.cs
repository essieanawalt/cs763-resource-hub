// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Test host with a fresh database per class, a controllable clock, the test JWKS, a throwaway field key and captured logs (~100%)
// Human Modifications: None yet
// Verification: Used by every database test in ResourceHub.Tests (dotnet test against PostgreSQL 18)
// Confidence: High

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using ResourceHub.Api.Data;

namespace ResourceHub.Tests.Infrastructure;

/// <summary>A clock the tests can move forward (for due dates and overdue devices).</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>Keeps every log line the API writes so tests can check what reached the log.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner._lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}

/// <summary>
/// Runs the API in memory against its own PostgreSQL database, created (and migrated and
/// seeded) at startup and dropped when the test class finishes.
/// </summary>
public class HubApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>A random AES-256 key for this test run only.</summary>
    public static readonly string FieldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly string _databaseName = $"rh_test_{Guid.NewGuid():N}";

    public TestClock Clock { get; } = new();

    public CapturedLogs Logs { get; } = new();

    protected virtual string ConnectionString => TestDb.ForDatabase(_databaseName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:HubDb", ConnectionString);
        builder.UseSetting("JWT_JWKS_PATH", TestTokens.JwksPath);
        builder.UseSetting("JWT_ISSUER", TestTokens.Issuer);
        builder.UseSetting("JWT_AUDIENCE", TestTokens.Audience);
        builder.UseSetting("FIELD_ENCRYPTION_KEY", FieldKey);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
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

    /// <summary>Runs <paramref name="work"/> with a DbContext on this class's database.</summary>
    public async Task<T> WithDbAsync<T>(Func<HubDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<HubDbContext>());
    }

    /// <summary>Opens a raw connection to this class's database (for looking at stored bytes).</summary>
    public async Task<NpgsqlConnection> OpenRawAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
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
