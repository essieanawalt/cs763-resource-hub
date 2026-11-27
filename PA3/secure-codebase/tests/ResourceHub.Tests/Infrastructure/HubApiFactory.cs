// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Test host with a fresh database per class owned by its own owner role, the API connected as a separate runtime role (migrations run as the owner), a controllable clock, the test JWKS, a throwaway field key and captured logs (~100%)
// Human Modifications: None yet
// Verification: Used by every database test in ResourceHub.Tests (dotnet test against PostgreSQL 18); DatabaseRoleTests
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
/// Runs the API in memory against its own PostgreSQL database. The database belongs to an owner
/// role made for this class, which applies the migrations and seed data at startup; the API itself
/// connects as a separate runtime role with data access only, as in Compose. Everything is dropped
/// when the test class finishes.
/// </summary>
public class HubApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>A random AES-256 key for this test run only.</summary>
    public static readonly string FieldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly string _databaseName = $"rh_test_{Guid.NewGuid():N}";

    public TestClock Clock { get; } = new();

    public CapturedLogs Logs { get; } = new();

    /// <summary>Owns the database and its tables; runs the migrations.</summary>
    public DbRole Owner { get; } = DbRole.New("owner");

    /// <summary>The role the API connects as.</summary>
    public DbRole Runtime { get; } = DbRole.New("app");

    public string DatabaseName => _databaseName;

    /// <summary>The API's connection: the runtime role.</summary>
    protected virtual string ConnectionString => TestDb.ForRole(_databaseName, Runtime.Name, Runtime.Password);

    /// <summary>The migration connection: the owner role. Null means the API gets none.</summary>
    protected virtual string? OwnerConnectionString => TestDb.ForRole(_databaseName, Owner.Name, Owner.Password);

    /// <summary>False for factories that point at a server that does not exist.</summary>
    protected virtual bool CreatesDatabase => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:HubDb", ConnectionString);
        builder.UseSetting("ConnectionStrings:HubDbOwner", OwnerConnectionString ?? "");
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

    public virtual async Task InitializeAsync()
    {
        if (CreatesDatabase && TestDb.BaseConnectionString is not null)
        {
            await TestDb.CreateDatabaseWithRolesAsync(_databaseName, Owner, Runtime);
        }

        // Starting the host applies the migrations and seed data (as the owner role).
        _ = Server;
    }

    /// <summary>Runs <paramref name="work"/> with the API's own DbContext (the runtime role).</summary>
    public async Task<T> WithDbAsync<T>(Func<HubDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<HubDbContext>());
    }

    /// <summary>
    /// Opens a connection as the test server's superuser (for looking at stored bytes and for
    /// tampering tests that must get past table privileges to reach the triggers).
    /// </summary>
    public async Task<NpgsqlConnection> OpenRawAsync()
    {
        var connection = new NpgsqlConnection(TestDb.ForDatabase(_databaseName));
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Opens a connection as the runtime role, with the same rights as the API.</summary>
    public async Task<NpgsqlConnection> OpenAsRuntimeRoleAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (CreatesDatabase)
        {
            await TestDb.DropDatabaseAndRolesAsync(_databaseName, Owner.Name, Runtime.Name);
        }
    }
}

/// <summary>Points the API at a database server that does not exist.</summary>
public sealed class UnreachableDbFactory : HubApiFactory
{
    private const string Nowhere = "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=none;Timeout=2";

    protected override string ConnectionString => Nowhere;

    protected override string? OwnerConnectionString => Nowhere;

    protected override bool CreatesDatabase => false;
}
