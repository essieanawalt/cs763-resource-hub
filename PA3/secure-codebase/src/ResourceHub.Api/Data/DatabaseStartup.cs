// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Migration that encrypts any plaintext license keys and seeds the JetBrains keys encrypted before the plaintext column is dropped; runs only over the owner role's connection and then grants the runtime role data access and nothing else (CWE-250, CWE-269) (~100%)
// Human Modifications: None yet
// Verification: DatabaseRoleTests, MigrationJobTests, LicenseKeyProtectionTests (seeded keys stored as ciphertext), SeedDataTests, every test database migrates through it; ComposeStackTests
// Confidence: Medium (the legacy plaintext path is exercised only on databases created by the PA2 schema)

using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using ResourceHub.Api.Security;

namespace ResourceHub.Api.Data;

public static partial class DatabaseStartup
{
    /// <summary>Connection string of the role that owns the schema; only the migration step gets it.</summary>
    public const string OwnerConnectionName = "HubDbOwner";

    /// <summary>The login role the API runs as; the migration grants it data access.</summary>
    public const string RuntimeRoleKey = "Database:RuntimeRole";

    /// <summary>Append-only history: the runtime role may read and add rows, never change or remove them.</summary>
    private static readonly string[] HistoryTables = ["ticket_events", "audit_events"];

    /// <summary>Adds the ciphertext column next to the old plaintext one.</summary>
    public const string EncryptLicenseKeysStep1 = "20261126100000_EncryptLicenseKeysStep1";

    /// <summary>Drops the plaintext column and adds the PA3 tables and constraints.</summary>
    public const string SecurityHardening = "20261126100100_SecurityHardening";

    /// <summary>
    /// Applies the migrations. Between step 1 and the hardening migration, existing plaintext
    /// keys (from a PA2 database) are encrypted and the seeded keys are inserted encrypted, so a
    /// key value is never lost and never stored in clear by this version.
    /// </summary>
    public static async Task MigrateAsync(HubDbContext db, FieldEncryptor encryptor, CancellationToken ct)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Contains(SecurityHardening))
        {
            await db.GetService<IMigrator>().MigrateAsync(EncryptLicenseKeysStep1, cancellationToken: ct);
            await EncryptAndSeedLicenseKeysAsync(db, encryptor, ct);
        }
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Migrates over the owner role's connection, then grants <paramref name="runtimeRole"/> the
    /// data access the API needs. Returns false if the grants were skipped because the runtime
    /// role is the owner itself (a single-role local setup).
    /// </summary>
    public static async Task<bool> MigrateAsOwnerAsync(string ownerConnectionString, string runtimeRole, FieldEncryptor encryptor, CancellationToken ct)
    {
        ValidateRoleName(runtimeRole);
        var options = new DbContextOptionsBuilder<HubDbContext>().UseNpgsql(ownerConnectionString).Options;
        await using var db = new HubDbContext(options);
        await MigrateAsync(db, encryptor, ct);
        return await GrantRuntimeAccessAsync(db, runtimeRole, ct);
    }

    /// <summary>The runtime role: Database:RuntimeRole, or the user name in ConnectionStrings:HubDb.</summary>
    public static string? RuntimeRoleFrom(IConfiguration configuration)
    {
        var configured = configuration[RuntimeRoleKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }
        var runtime = configuration.GetConnectionString("HubDb");
        return string.IsNullOrWhiteSpace(runtime) ? null : new Npgsql.NpgsqlConnectionStringBuilder(runtime).Username;
    }

    /// <summary>Role names go into GRANT statements, which take no parameters: allow plain identifiers only.</summary>
    public static void ValidateRoleName(string? role)
    {
        if (string.IsNullOrEmpty(role) || !RoleName().IsMatch(role))
        {
            throw new InvalidOperationException($"{RuntimeRoleKey} must be a plain PostgreSQL role name (letters, digits, underscore; at most 63 characters).");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex RoleName();

    private static async Task<bool> GrantRuntimeAccessAsync(HubDbContext db, string runtimeRole, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        var transaction = tx.GetDbTransaction();

        string database;
        await using (var who = Command(connection, transaction,
            "SELECT current_user, current_database(), (SELECT rolsuper FROM pg_roles WHERE rolname = @role)"))
        {
            Add(who, "role", runtimeRole);
            await using var reader = await who.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var owner = reader.GetString(0);
            if (owner == runtimeRole)
            {
                return false;
            }
            if (reader.IsDBNull(2))
            {
                throw new InvalidOperationException($"The runtime role named by {RuntimeRoleKey} does not exist.");
            }
            if (reader.GetBoolean(2))
            {
                throw new InvalidOperationException($"The runtime role named by {RuntimeRoleKey} is a superuser; it must be an ordinary login role.");
            }
            database = reader.GetString(1);
        }

        var role = Quote(runtimeRole);
        var history = string.Join(", ", HistoryTables);
        // Revoke first so the result is the same however often this runs. No CREATE on the schema,
        // no TRUNCATE, TRIGGER or REFERENCES, and no ownership: the role cannot change the schema or
        // switch triggers off (only a table's owner can).
        var sql = $"""
            REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {role};
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {role};
            REVOKE ALL ON SCHEMA public FROM {role};
            REVOKE CREATE, TEMPORARY ON DATABASE {Quote(database)} FROM {role};
            GRANT CONNECT ON DATABASE {Quote(database)} TO {role};
            GRANT USAGE ON SCHEMA public TO {role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {role};
            REVOKE UPDATE, DELETE ON {history} FROM {role};
            REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM {role};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {role};
            """;
        await using (var grant = Command(connection, transaction, sql))
        {
            await grant.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Double-quotes an identifier (after <see cref="ValidateRoleName"/> or from current_database()).</summary>
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static async Task EncryptAndSeedLicenseKeysAsync(HubDbContext db, FieldEncryptor encryptor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        var transaction = tx.GetDbTransaction();

        // 1. Legacy rows: encrypt, then clear the plaintext.
        var legacy = new List<(Guid Id, string Key)>();
        await using (var select = Command(connection, transaction, "SELECT \"Id\", \"Key\" FROM license_keys WHERE \"Key\" IS NOT NULL FOR UPDATE"))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                legacy.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }
        foreach (var (id, key) in legacy)
        {
            await using var update = Command(connection, transaction,
                "UPDATE license_keys SET \"KeyCiphertext\" = @c, \"Key\" = NULL WHERE \"Id\" = @id");
            Add(update, "c", encryptor.Encrypt(key, id));
            Add(update, "id", id);
            await update.ExecuteNonQueryAsync(ct);
        }

        // 2. Seeded keys (spec section 7), encrypted, if their asset exists and they are not there yet.
        foreach (var (id, assetId, key) in SeedData.LicenseKeys)
        {
            await using var insert = Command(connection, transaction, """
                INSERT INTO license_keys ("Id", "AssetId", "KeyCiphertext")
                SELECT @id, @asset, @c
                WHERE EXISTS (SELECT 1 FROM assets WHERE "Id" = @asset)
                  AND NOT EXISTS (SELECT 1 FROM license_keys WHERE "Id" = @id)
                """);
            Add(insert, "id", id);
            Add(insert, "asset", assetId);
            Add(insert, "c", encryptor.Encrypt(key, id));
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }
}
