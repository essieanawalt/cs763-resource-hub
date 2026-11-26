// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Startup migration that encrypts any plaintext license keys and seeds the JetBrains keys encrypted before the plaintext column is dropped (~100%)
// Human Modifications: None yet
// Verification: LicenseKeyProtectionTests (seeded keys stored as ciphertext), SeedDataTests, every test database migrates through it
// Confidence: Medium (the legacy plaintext path is exercised only on databases created by the PA2 schema)

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using ResourceHub.Api.Security;

namespace ResourceHub.Api.Data;

public static class DatabaseStartup
{
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
