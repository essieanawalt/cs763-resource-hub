// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: One-shot migration job (dotnet ResourceHub.Api.dll --migrate) that Compose runs as the owner role in its own container, so the API container never holds the owner's credentials (CWE-250, CWE-269) (~100%)
// Human Modifications: None yet
// Verification: MigrationJobTests (dotnet test against PostgreSQL 18); ComposeStackTests.Migration_job_ran_as_the_owner_and_exited_cleanly
// Confidence: High

using ResourceHub.Api.Security;

namespace ResourceHub.Api.Data;

/// <summary>
/// Applies the migrations (including encrypting any plaintext license keys) over
/// ConnectionStrings:HubDbOwner and grants the role in Database:RuntimeRole data access, then exits.
/// It needs no JWKS, issuer or runtime password. Messages never include a connection string.
/// </summary>
public static class MigrationJob
{
    public const string Argument = "--migrate";

    /// <summary>0 when done; 1 when the database step failed; 2 when the job is misconfigured.</summary>
    public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output, CancellationToken ct)
    {
        var ownerConnection = configuration.GetConnectionString(DatabaseStartup.OwnerConnectionName);
        if (string.IsNullOrWhiteSpace(ownerConnection))
        {
            await output.WriteLineAsync($"migrate: ConnectionStrings:{DatabaseStartup.OwnerConnectionName} is not set.");
            return 2;
        }

        var runtimeRole = configuration[DatabaseStartup.RuntimeRoleKey];
        FieldEncryptor encryptor;
        try
        {
            if (string.IsNullOrWhiteSpace(runtimeRole))
            {
                throw new InvalidOperationException($"{DatabaseStartup.RuntimeRoleKey} is not set (the role the API connects as).");
            }
            DatabaseStartup.ValidateRoleName(runtimeRole);
            encryptor = FieldEncryptor.FromConfiguration(configuration);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync("migrate: " + ex.Message);
            return 2;
        }

        try
        {
            var granted = await DatabaseStartup.MigrateAsOwnerAsync(ownerConnection, runtimeRole, encryptor, ct);
            await output.WriteLineAsync(granted
                ? $"migrate: schema is up to date; runtime role {runtimeRole} has data access only."
                : "migrate: schema is up to date; the runtime role is the owner, so no grants were made.");
            return 0;
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException)
        {
            // The exception text from Npgsql does not include the password; the connection string is never printed.
            await output.WriteLineAsync($"migrate: failed ({ex.GetType().Name}): {ex.Message}");
            return 1;
        }
    }
}
