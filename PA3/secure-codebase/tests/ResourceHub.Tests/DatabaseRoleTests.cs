// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Tests that the API's runtime role owns nothing and cannot create, alter or drop tables, triggers or functions, cannot bypass the history triggers, keeps the data access it needs, and that only the owner role migrates (CWE-250, CWE-269) (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18 (failed to compile before MigrationJob and the owner connection existed; passes after)
// Confidence: High

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using ResourceHub.Api.Data;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class DatabaseRoleTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private const string InsufficientPrivilege = "42501";

    /// <summary>Schema changes and trigger bypasses the runtime role must not be able to make.</summary>
    public static TheoryData<string> ForbiddenStatements => new()
    {
        "ALTER TABLE ticket_events DISABLE TRIGGER ticket_events_append_only",
        "ALTER TABLE audit_events DISABLE TRIGGER ALL",
        "ALTER TABLE audit_events DISABLE TRIGGER USER",
        "DROP TRIGGER audit_events_append_only ON audit_events",
        "DROP TRIGGER ticket_events_no_truncate ON ticket_events",
        "DROP FUNCTION reject_history_change() CASCADE",
        "CREATE OR REPLACE FUNCTION reject_history_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN OLD; END $$",
        "DROP TABLE audit_events",
        "DROP TABLE ticket_events CASCADE",
        "ALTER TABLE tickets ADD COLUMN sneaky integer",
        "ALTER TABLE ticket_events OWNER TO CURRENT_USER",
        "ALTER TABLE license_keys DROP CONSTRAINT ck_license_keys_ciphertext_format",
        "CREATE TABLE sneaky (id integer)",
        "CREATE SCHEMA sneaky",
        "CREATE TRIGGER sneaky BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION reject_history_change()",
        "TRUNCATE ticket_events",
        "TRUNCATE audit_events",
        "UPDATE ticket_events SET \"ActorId\" = NULL",
        "DELETE FROM audit_events",
        "UPDATE \"__EFMigrationsHistory\" SET \"ProductVersion\" = 'x'",
        "SET session_replication_role = replica",
    };

    [DbTheory]
    [MemberData(nameof(ForbiddenStatements))]
    public async Task Runtime_role_cannot_change_the_schema_or_bypass_the_history_triggers(string sql)
    {
        await using var connection = await factory.OpenAsRuntimeRoleAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(InsufficientPrivilege, ex.SqlState);
    }

    [DbFact]
    public async Task Runtime_role_owns_nothing_and_has_no_create_rights()
    {
        await using var connection = await factory.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                 WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) = @app),
              (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                 WHERE n.nspname = 'public' AND pg_get_userbyid(p.proowner) = @app),
              (SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner <> @owner),
              (SELECT count(*) FROM pg_tables WHERE schemaname = 'public'),
              has_schema_privilege(@app, 'public', 'CREATE'),
              has_database_privilege(@app, current_database(), 'CREATE'),
              has_database_privilege(@app, current_database(), 'TEMPORARY'),
              pg_get_userbyid((SELECT datdba FROM pg_database WHERE datname = current_database())),
              (SELECT rolsuper OR rolcreaterole OR rolcreatedb OR rolbypassrls FROM pg_roles WHERE rolname = @app)
            """;
        command.Parameters.AddWithValue("app", factory.Runtime.Name);
        command.Parameters.AddWithValue("owner", factory.Owner.Name);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(0L, reader.GetInt64(0));            // no table, index or sequence
        Assert.Equal(0L, reader.GetInt64(1));            // no function (trigger functions included)
        Assert.Equal(0L, reader.GetInt64(2));            // every table belongs to the owner role
        Assert.True(reader.GetInt64(3) >= 12);           // ...and the migrations did create them
        Assert.False(reader.GetBoolean(4));
        Assert.False(reader.GetBoolean(5));
        Assert.False(reader.GetBoolean(6));
        Assert.Equal(factory.Owner.Name, reader.GetString(7));
        Assert.False(reader.GetBoolean(8));
    }

    [DbFact]
    public async Task Runtime_role_keeps_only_the_data_access_the_service_needs()
    {
        await using var connection = await factory.OpenRawAsync();

        async Task<bool> Has(string table, string privilege)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT has_table_privilege(@role, @table, @privilege)";
            command.Parameters.AddWithValue("role", factory.Runtime.Name);
            command.Parameters.AddWithValue("table", "public.\"" + table + "\"");
            command.Parameters.AddWithValue("privilege", privilege);
            return (bool)(await command.ExecuteScalarAsync())!;
        }

        foreach (var table in new[] { "assets", "tickets", "ticket_items", "allocations", "device_assignments", "license_keys", "users", "courses", "research_labs", "projects" })
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                Assert.True(await Has(table, privilege), $"{privilege} on {table}");
            }
            Assert.False(await Has(table, "TRUNCATE"), $"TRUNCATE on {table}");
            Assert.False(await Has(table, "TRIGGER"), $"TRIGGER on {table}");
            Assert.False(await Has(table, "REFERENCES"), $"REFERENCES on {table}");
        }

        // History tables: append and read only, even before the triggers are reached.
        foreach (var table in new[] { "ticket_events", "audit_events" })
        {
            Assert.True(await Has(table, "SELECT"));
            Assert.True(await Has(table, "INSERT"));
            Assert.False(await Has(table, "UPDATE"), $"UPDATE on {table}");
            Assert.False(await Has(table, "DELETE"), $"DELETE on {table}");
            Assert.False(await Has(table, "TRUNCATE"), $"TRUNCATE on {table}");
        }

        Assert.True(await Has("__EFMigrationsHistory", "SELECT"));
        Assert.False(await Has("__EFMigrationsHistory", "INSERT"));
    }

    [DbFact]
    public async Task Api_connects_as_the_runtime_role_and_still_serves_reads_and_writes()
    {
        var who = await factory.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT current_user";
            return (string)(await command.ExecuteScalarAsync())!;
        });
        Assert.Equal(factory.Runtime.Name, who);

        var client = factory.CreateClient();
        await (await client.GetAsync("/api/v1/assets", Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);
        // Submitting writes a ticket, its items, a ticket event and an audit event.
        var s = new Scenario(client);
        var ticket = await s.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        await s.ApproveAsync(ticket.Id());
        await (await s.AllocateAsync(ticket.Id())).AssertStatusAsync(HttpStatusCode.OK);
    }
}

/// <summary>The API starts without the owner connection, as in Compose, and then leaves the schema alone.</summary>
public sealed class NoOwnerConnectionFactory : HubApiFactory
{
    protected override string? OwnerConnectionString => null;
}

public class NoOwnerConnectionTests(NoOwnerConnectionFactory factory) : IClassFixture<NoOwnerConnectionFactory>
{
    [DbFact]
    public async Task Without_the_owner_connection_the_api_does_not_try_to_migrate()
    {
        // The database is reachable (health is about connectivity) but nobody created the tables.
        await (await factory.CreateClient().GetAsync("/api/v1/health")).AssertStatusAsync(HttpStatusCode.OK);

        await using var connection = await factory.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.assets') IS NULL";
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
        Assert.Contains(factory.Logs.Lines, l => l.Contains("migration job", StringComparison.Ordinal));
    }
}

/// <summary>The one-shot migration job (dotnet ResourceHub.Api.dll --migrate) that Compose runs as the owner.</summary>
public sealed class MigrationJobTests : IAsyncLifetime
{
    private readonly string _database = $"rh_test_{Guid.NewGuid():N}";
    private readonly DbRole _owner = DbRole.New("owner");
    private readonly DbRole _runtime = DbRole.New("app");

    public async Task InitializeAsync()
    {
        if (TestDb.BaseConnectionString is not null)
        {
            await TestDb.CreateDatabaseWithRolesAsync(_database, _owner, _runtime);
        }
    }

    public Task DisposeAsync() => TestDb.DropDatabaseAndRolesAsync(_database, _owner.Name, _runtime.Name);

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [DbFact]
    public async Task Migrates_as_the_owner_and_grants_the_runtime_role_data_access_only()
    {
        using var output = new StringWriter();
        var exit = await MigrationJob.RunAsync(Config(
            ("ConnectionStrings:HubDbOwner", TestDb.ForRole(_database, _owner.Name, _owner.Password)),
            ("Database:RuntimeRole", _runtime.Name),
            ("FIELD_ENCRYPTION_KEY", HubApiFactory.FieldKey)), output, CancellationToken.None);

        Assert.True(exit == 0, output.ToString());
        Assert.DoesNotContain(_owner.Password, output.ToString());

        await using var app = new NpgsqlConnection(TestDb.ForRole(_database, _runtime.Name, _runtime.Password));
        await app.OpenAsync();
        await using var read = app.CreateCommand();
        read.CommandText = "SELECT count(*) FROM assets";
        Assert.True((long)(await read.ExecuteScalarAsync())! >= 4);   // seeded catalog, readable
        await using var drop = app.CreateCommand();
        drop.CommandText = "DROP TABLE audit_events";
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => drop.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task Refuses_to_run_without_the_owner_connection_or_the_runtime_role()
    {
        using var output = new StringWriter();
        Assert.NotEqual(0, await MigrationJob.RunAsync(Config(
            ("Database:RuntimeRole", "resourcehub_app"),
            ("FIELD_ENCRYPTION_KEY", HubApiFactory.FieldKey)), output, CancellationToken.None));
        Assert.Contains("HubDbOwner", output.ToString());

        output.GetStringBuilder().Clear();
        Assert.NotEqual(0, await MigrationJob.RunAsync(Config(
            ("ConnectionStrings:HubDbOwner", "Host=127.0.0.1;Port=1;Database=x;Username=o;Password=secret-owner-pw;Timeout=2"),
            ("FIELD_ENCRYPTION_KEY", HubApiFactory.FieldKey)), output, CancellationToken.None));
        Assert.Contains("RuntimeRole", output.ToString());
    }

    [Theory]
    [InlineData("resourcehub_app; DROP TABLE assets")]
    [InlineData("\"quoted\"")]
    [InlineData("")]
    public void Runtime_role_name_must_be_a_plain_identifier(string role)
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseStartup.ValidateRoleName(role));
    }

    [Fact]
    public async Task Unreachable_database_fails_without_printing_the_password()
    {
        using var output = new StringWriter();
        var exit = await MigrationJob.RunAsync(Config(
            ("ConnectionStrings:HubDbOwner", "Host=127.0.0.1;Port=1;Database=x;Username=o;Password=secret-owner-pw;Timeout=2"),
            ("Database:RuntimeRole", "resourcehub_app"),
            ("FIELD_ENCRYPTION_KEY", HubApiFactory.FieldKey)), output, CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("secret-owner-pw", output.ToString());
    }
}
