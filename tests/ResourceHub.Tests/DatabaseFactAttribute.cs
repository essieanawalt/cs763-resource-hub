namespace ResourceHub.Tests;

// Runs only when RESOURCEHUB_TEST_DB holds a connection string to a live PostgreSQL.
public sealed class DatabaseFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "RESOURCEHUB_TEST_DB";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(EnvironmentVariable);

    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = $"Set {EnvironmentVariable} to run tests that need PostgreSQL.";
        }
    }
}
