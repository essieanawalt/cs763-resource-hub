// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Test database settings and the skip-when-no-database fact attribute (~100%)
// Human Modifications: None (review only)
// Verification: dotnet test with and without RESOURCEHUB_TEST_DB set; reviewed by me 2026-11-15
// Confidence: High

using Npgsql;

namespace ResourceHub.Tests.Infrastructure;

/// <summary>Reads the test database connection string from RESOURCEHUB_TEST_DB.</summary>
public static class TestDb
{
    public const string EnvironmentVariable = "RESOURCEHUB_TEST_DB";

    public static string? BaseConnectionString =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } value ? value : null;

    /// <summary>Same server and credentials, different database name.</summary>
    public static string ForDatabase(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = databaseName };
        return builder.ConnectionString;
    }
}

/// <summary>A fact that needs PostgreSQL; it is skipped when RESOURCEHUB_TEST_DB is not set.</summary>
public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        if (TestDb.BaseConnectionString is null)
        {
            Skip = $"{TestDb.EnvironmentVariable} is not set; this test needs PostgreSQL.";
        }
    }
}

/// <summary>A theory that needs PostgreSQL; it is skipped when RESOURCEHUB_TEST_DB is not set.</summary>
public sealed class DbTheoryAttribute : TheoryAttribute
{
    public DbTheoryAttribute()
    {
        if (TestDb.BaseConnectionString is null)
        {
            Skip = $"{TestDb.EnvironmentVariable} is not set; this test needs PostgreSQL.";
        }
    }
}
