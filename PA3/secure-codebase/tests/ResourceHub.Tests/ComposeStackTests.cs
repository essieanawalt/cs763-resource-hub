// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Static checks of the Compose and settings files (CWE-1392/1327/250/260/269/526) and live tests against the Compose stack with a test JWKS: owner-only migrations, a runtime role that cannot change the schema or triggers, no owner credentials or plain key in the API container, the key read from a secret file, and a healthy API (~100%)
// Human Modifications: None yet
// Verification: dotnet test; live part with RESOURCEHUB_COMPOSE_TESTS=1 against docker compose
// Confidence: Medium (live part depends on Docker being available)

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

/// <summary>Finds the working folder (the one holding docker-compose.yml and src/).</summary>
public static class WorkingFolder
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "docker-compose.yml"))
                && Directory.Exists(System.IO.Path.Combine(dir.FullName, "src", "ResourceHub.Api")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find the folder holding docker-compose.yml and src/ResourceHub.Api.");
    }

    public static string Read(string relative) => File.ReadAllText(System.IO.Path.Combine(Path, relative));
}

public class ComposeConfigTests
{
    private static readonly string Compose = WorkingFolder.Read("docker-compose.yml");

    private static string Service(string name)
    {
        var lines = Compose.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == $"  {name}:");
        Assert.True(start >= 0, $"service {name} not found");
        var end = Array.FindIndex(lines, start + 1, l => l.Length > 2 && !l.StartsWith("   ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(l));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    [Fact]
    public void Database_port_is_not_published()
    {
        var db = Service("db");
        Assert.DoesNotContain("ports:", db);
        Assert.Contains("internal: true", Compose);
    }

    private static string Init => WorkingFolder.Read(System.IO.Path.Combine("db", "init", "01-app-role.sh"));

    [Fact]
    public void No_password_has_a_committed_fallback()
    {
        Assert.DoesNotContain("resourcehub_dev", Compose);
        foreach (var variable in new[] { "POSTGRES_PASSWORD", "APP_DB_PASSWORD", "OWNER_DB_PASSWORD", "FIELD_ENCRYPTION_KEY_HOST_FILE", "JWKS_HOST_PATH" })
        {
            Assert.Contains($"${{{variable}:?", Compose);
            Assert.DoesNotContain($"${{{variable}:-", Compose);
        }
    }

    [Fact]
    public void Api_connects_as_the_non_superuser_app_role()
    {
        var api = Service("api");
        Assert.Contains("Username=${APP_DB_USER", api);
        Assert.DoesNotContain("POSTGRES_PASSWORD", api);
        Assert.Contains("NOSUPERUSER", Init);
        Assert.Contains("NOCREATEROLE", Init);
        Assert.Contains("NOCREATEDB", Init);
    }

    [Fact]
    public void Only_the_migration_job_gets_the_owner_role_and_the_api_waits_for_it()
    {
        var api = Service("api");
        Assert.DoesNotContain("OWNER_DB", api);
        Assert.DoesNotContain("HubDbOwner", api);
        Assert.Contains("service_completed_successfully", api);

        var migrate = Service("migrate");
        Assert.Contains("--migrate", migrate);
        Assert.Contains("ConnectionStrings__HubDbOwner", migrate);
        Assert.Contains("Username=${OWNER_DB_USER", migrate);
        Assert.Contains("Database__RuntimeRole: ${APP_DB_USER", migrate);
        Assert.DoesNotContain("APP_DB_PASSWORD", migrate);
        Assert.DoesNotContain("POSTGRES_PASSWORD", migrate);
        Assert.DoesNotContain("ports:", migrate);
    }

    [Fact]
    public void Database_belongs_to_the_owner_role_not_the_app_role()
    {
        Assert.Contains("CREATE DATABASE :\"app_db\" OWNER :\"owner_user\"", Init);
        Assert.Contains("ALTER SCHEMA public OWNER TO :\"owner_user\"", Init);
        Assert.DoesNotContain("OWNER :\"app_user\"", Init);
        Assert.DoesNotContain("OWNER TO :\"app_user\"", Init);
        Assert.DoesNotContain("TEMPORARY", Init);
    }

    [Fact]
    public void Jwks_is_mounted_read_only_and_api_is_bound_to_localhost()
    {
        var api = Service("api");
        Assert.Contains("read_only: true", api);
        Assert.Contains("JWT_JWKS_PATH", api);
        Assert.Contains("JWT_ISSUER", api);
        Assert.Contains("JWT_AUDIENCE", api);
        Assert.Contains("127.0.0.1:", api);
    }

    [Fact]
    public void Field_key_reaches_the_containers_only_as_a_secret_file()
    {
        foreach (var name in new[] { "api", "migrate" })
        {
            var service = Service(name);
            Assert.Contains("FIELD_ENCRYPTION_KEY_FILE: /run/secrets/field_encryption_key", service);
            Assert.Contains("- field_encryption_key", service);
            Assert.DoesNotContain("FIELD_ENCRYPTION_KEY:", service);
            Assert.DoesNotContain("${FIELD_ENCRYPTION_KEY}", service);
        }
        Assert.Contains("file: ${FIELD_ENCRYPTION_KEY_HOST_FILE:?", Compose);
    }

    [Fact]
    public void Api_has_a_health_check_that_needs_no_extra_packages()
    {
        var api = Service("api");
        Assert.Contains("healthcheck:", api);
        Assert.Contains("\"--healthcheck\"", api);
        var dockerfile = WorkingFolder.Read("Dockerfile");
        foreach (var tool in new[] { "curl", "wget", "apt-get", "apk " })
        {
            Assert.DoesNotContain(tool, api);
            Assert.DoesNotContain(tool, dockerfile);
        }
    }

    [Fact]
    public void Env_example_lists_every_variable_with_placeholders_only()
    {
        var example = WorkingFolder.Read(".env.example");
        foreach (var variable in new[] { "POSTGRES_PASSWORD", "OWNER_DB_USER", "OWNER_DB_PASSWORD", "APP_DB_USER", "APP_DB_PASSWORD", "FIELD_ENCRYPTION_KEY_HOST_FILE", "JWKS_HOST_PATH", "JWT_JWKS_PATH", "JWT_ISSUER", "JWT_AUDIENCE", "API_PORT" })
        {
            Assert.Contains(variable + "=", example);
        }
        var lines = example.Split('\n').Select(l => l.Trim()).ToList();
        // The key itself is no longer an environment value for Compose, not even a placeholder.
        Assert.DoesNotContain(lines, l => l.StartsWith("FIELD_ENCRYPTION_KEY=", StringComparison.Ordinal));
        foreach (var secret in new[] { "POSTGRES_PASSWORD=", "OWNER_DB_PASSWORD=", "APP_DB_PASSWORD=", "FIELD_ENCRYPTION_KEY_HOST_FILE=" })
        {
            Assert.StartsWith(secret + "<", lines.Single(l => l.StartsWith(secret, StringComparison.Ordinal)));
        }
        var gitignore = WorkingFolder.Read(".gitignore").Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains(".env", gitignore);
        Assert.Contains("secrets/", gitignore);
    }

    [Fact]
    public void Development_settings_hold_no_password()
    {
        var dev = WorkingFolder.Read(System.IO.Path.Combine("src", "ResourceHub.Api", "appsettings.Development.json"));
        Assert.DoesNotContain("Password", dev, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Builds and starts the Compose stack with a throwaway JWKS, field key and passwords, then
/// tears it down. Runs only when RESOURCEHUB_COMPOSE_TESTS=1 (it needs Docker).
/// </summary>
public sealed class ComposeStack : IAsyncLifetime
{
    public const string EnvironmentVariable = "RESOURCEHUB_COMPOSE_TESTS";
    public static bool Enabled => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";

    private readonly string _project = $"rh-pa3-e2e-{Guid.NewGuid():N}"[..20];
    private readonly Dictionary<string, string> _env = [];

    public const string OwnerUser = "resourcehub_owner";
    public const string AppUser = "resourcehub_app";

    public HttpClient Client { get; } = new();
    public string FieldKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string OwnerPassword { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public string AppPassword { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public async Task InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        // Tests point JWKS_HOST_PATH at the test JWKS file (public keys of the throwaway pair).
        var jwksDir = System.IO.Path.Combine(AppContext.BaseDirectory, "compose-test-jwks");
        Directory.CreateDirectory(jwksDir);
        var jwks = System.IO.Path.Combine(jwksDir, "jwks.json");
        TestTokens.WriteJwks(jwks);

        var port = FreePort();
        // The field key goes in as a secret file, readable only by the current user.
        var keyFile = System.IO.Path.Combine(jwksDir, "field_encryption_key");
        File.WriteAllText(keyFile, FieldKey + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        _env["JWKS_HOST_PATH"] = jwks;
        _env["FIELD_ENCRYPTION_KEY_HOST_FILE"] = keyFile;
        _env["POSTGRES_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _env["OWNER_DB_USER"] = OwnerUser;
        _env["OWNER_DB_PASSWORD"] = OwnerPassword;
        _env["APP_DB_USER"] = AppUser;
        _env["APP_DB_PASSWORD"] = AppPassword;
        _env["JWT_JWKS_PATH"] = "/etc/resourcehub/jwks.json";
        _env["JWT_ISSUER"] = TestTokens.Issuer;
        _env["JWT_AUDIENCE"] = TestTokens.Audience;
        _env["API_PORT"] = port.ToString();
        Client.BaseAddress = new Uri($"http://127.0.0.1:{port}");

        await RunAsync("up", "-d", "--build");
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (true)
        {
            try
            {
                if ((await Client.GetAsync("/api/v1/health")).StatusCode == HttpStatusCode.OK)
                {
                    break;
                }
            }
            catch (HttpRequestException)
            {
            }
            if (DateTime.UtcNow > deadline)
            {
                var logs = await RunAsync("logs", "--no-color", "migrate", "api");
                throw new InvalidOperationException("Compose stack did not become healthy:\n" + logs.Output);
            }
            await Task.Delay(1000);
        }
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (Enabled)
        {
            await RunAsync("down", "-v", "--remove-orphans");
        }
    }

    public async Task<(int Exit, string Output)> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("docker")
        {
            WorkingDirectory = WorkingFolder.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "compose", "-p", _project, "-f", "docker-compose.yml" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }
        foreach (var (k, v) in _env)
        {
            psi.Environment[k] = v;
        }
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>docker inspect of a service's container (also finds the exited migration job).</summary>
    public async Task<string> InspectAsync(string service, string format)
    {
        var (_, id) = await RunAsync("ps", "-a", "-q", service);
        Assert.False(string.IsNullOrWhiteSpace(id), $"no container for {service}");
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "inspect", "--format", format, id.Trim() })
        {
            psi.ArgumentList.Add(a);
        }
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    /// <summary>Runs one statement with psql inside the db container, logged in over TCP as <paramref name="user"/>.</summary>
    public Task<(int Exit, string Output)> PsqlAsAsync(string user, string password, string sql) =>
        RunAsync("exec", "-T", "-e", "PGPASSWORD=" + password, "db",
            "psql", "-h", "127.0.0.1", "-U", user, "-d", "resourcehub", "-v", "ON_ERROR_STOP=1", "-At", "-c", sql);

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed class ComposeFactAttribute : FactAttribute
{
    public ComposeFactAttribute()
    {
        if (!ComposeStack.Enabled)
        {
            Skip = $"{ComposeStack.EnvironmentVariable} is not 1; this test builds and runs the Compose stack.";
        }
    }
}

public class ComposeStackTests(ComposeStack stack) : IClassFixture<ComposeStack>
{
    private HttpClient Client => stack.Client;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await Client.SendAsync(request);
    }

    [ComposeFact]
    public async Task Stack_serves_health_publicly_and_requires_tokens_elsewhere()
    {
        await (await Client.GetAsync("/api/v1/health")).AssertStatusAsync(HttpStatusCode.OK);
        await (await SendAsync(HttpMethod.Get, "/api/v1/assets", null)).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await SendAsync(HttpMethod.Get, "/api/v1/assets", TestTokens.For(Ids.Admin))).AssertStatusAsync(HttpStatusCode.OK);
        await (await SendAsync(HttpMethod.Get, "/api/v1/assets", TestTokens.Unsigned(TestTokens.Claims(Ids.Admin.ToString(), ["admin"]))))
            .AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await SendAsync(HttpMethod.Get, "/api/v1/assets", TestTokens.Sign(TestTokens.Claims(Ids.Admin.ToString(), ["admin"]), TestTokens.Header(kid: "nope"))))
            .AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [ComposeFact]
    public async Task Ticket_flow_works_end_to_end_with_row_level_checks()
    {
        var submit = await SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", TestTokens.For(Ids.InstructorChen), new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetJetBrains, quantity = 1 } },
        });
        await submit.AssertStatusAsync(HttpStatusCode.Created);
        var id = (await submit.JsonAsync()).Id();

        await (await SendAsync(HttpMethod.Get, $"/api/v1/assets/tickets/{id}", TestTokens.For(Ids.InstructorOkafor))).AssertStatusAsync(HttpStatusCode.NotFound);
        await (await SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{id}", TestTokens.For(Ids.InstructorChen), new { status = "approved" }))
            .AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{id}", TestTokens.For(Ids.Admin), new { status = "approved" }))
            .AssertStatusAsync(HttpStatusCode.OK);
        var allocate = await SendAsync(HttpMethod.Post, $"/api/v1/assets/tickets/{id}/allocate", TestTokens.For(Ids.Admin));
        await allocate.AssertStatusAsync(HttpStatusCode.OK);
        var key = (await allocate.JsonAsync()).GetProperty("allocations")[0].GetProperty("licenseKeys")[0].GetString()!;
        Assert.StartsWith("JBIDE-", key);

        // At rest in the Compose database, the key is ciphertext only.
        var (exit, output) = await stack.RunAsync("exec", "-T", "-u", "postgres", "db", "psql", "-d", "resourcehub", "-At", "-c",
            "SELECT \"KeyCiphertext\" FROM license_keys");
        Assert.Equal(0, exit);
        Assert.DoesNotContain("JBIDE", output);
        Assert.Contains("v1.", output);
    }

    [ComposeFact]
    public async Task Database_is_not_reachable_from_the_host_and_app_role_is_not_superuser()
    {
        // Unpublished: "docker compose port" prints nothing or ":0", and ps lists no host mapping.
        var (_, port) = await stack.RunAsync("port", "db", "5432");
        Assert.DoesNotMatch(@":[1-9]\d*", port.Trim());
        var (_, ports) = await stack.RunAsync("ps", "db", "--format", "{{.Ports}}");
        Assert.DoesNotContain("->", ports);

        var (exit, output) = await stack.RunAsync("exec", "-T", "-u", "postgres", "db", "psql", "-d", "resourcehub", "-At", "-c",
            $"SELECT rolsuper::text || ',' || rolcreaterole::text || ',' || rolcreatedb::text FROM pg_roles WHERE rolname = '{ComposeStack.AppUser}'");
        Assert.Equal(0, exit);
        Assert.Equal("false,false,false", output.Trim());
    }

    [ComposeFact]
    public async Task Api_container_reports_healthy()
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        string status;
        while ((status = (await stack.InspectAsync("api", "{{.State.Health.Status}}")).Trim()) != "healthy" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
        }
        Assert.Equal("healthy", status);
        var log = await stack.InspectAsync("api", "{{range .State.Health.Log}}{{.ExitCode}} {{end}}");
        Assert.EndsWith("0", log.Trim());
    }

    [ComposeFact]
    public async Task Migration_job_ran_as_the_owner_and_exited_cleanly()
    {
        Assert.Equal("exited 0", (await stack.InspectAsync("migrate", "{{.State.Status}} {{.State.ExitCode}}")).Trim());

        var (exit, owners) = await stack.RunAsync("exec", "-T", "-u", "postgres", "db", "psql", "-d", "resourcehub", "-At", "-c",
            "SELECT DISTINCT tableowner FROM pg_tables WHERE schemaname = 'public' UNION SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'resourcehub'");
        Assert.Equal(0, exit);
        Assert.Equal(ComposeStack.OwnerUser, owners.Trim());

        var (_, flags) = await stack.RunAsync("exec", "-T", "-u", "postgres", "db", "psql", "-d", "resourcehub", "-At", "-c",
            $"SELECT rolsuper::text || ',' || rolcreaterole::text || ',' || rolcreatedb::text FROM pg_roles WHERE rolname = '{ComposeStack.OwnerUser}'");
        Assert.Equal("false,false,false", flags.Trim());
    }

    [ComposeFact]
    public async Task Runtime_role_cannot_alter_or_drop_tables_or_triggers()
    {
        // The runtime role can do its job...
        var (ok, count) = await stack.PsqlAsAsync(ComposeStack.AppUser, stack.AppPassword, "SELECT count(*) FROM assets");
        Assert.Equal(0, ok);
        Assert.True(int.Parse(count.Trim(), System.Globalization.CultureInfo.InvariantCulture) >= 4);

        // ...but not change the schema or switch the history triggers off.
        foreach (var sql in new[]
        {
            "ALTER TABLE ticket_events DISABLE TRIGGER ALL",
            "ALTER TABLE audit_events DISABLE TRIGGER audit_events_append_only",
            "DROP TRIGGER audit_events_append_only ON audit_events",
            "DROP TABLE audit_events",
            "DROP TABLE ticket_events CASCADE",
            "ALTER TABLE tickets ADD COLUMN sneaky integer",
            "CREATE TABLE sneaky (id integer)",
            "TRUNCATE audit_events",
            "SET session_replication_role = replica",
        })
        {
            var (exit, output) = await stack.PsqlAsAsync(ComposeStack.AppUser, stack.AppPassword, sql);
            Assert.True(exit != 0, $"{sql} should have failed: {output}");
            Assert.True(output.Contains("must be owner", StringComparison.Ordinal) || output.Contains("permission denied", StringComparison.Ordinal),
                $"{sql}: {output}");
        }
    }

    [ComposeFact]
    public async Task Api_container_holds_no_owner_credentials_and_no_plain_key()
    {
        var env = await stack.InspectAsync("api", "{{json .Config.Env}}");
        var inspect = await stack.InspectAsync("api", "{{json .}}");
        foreach (var text in new[] { env, inspect })
        {
            Assert.DoesNotContain(stack.OwnerPassword, text);
            Assert.DoesNotContain(stack.FieldKey, text);
            Assert.DoesNotContain(ComposeStack.OwnerUser, text);
            Assert.DoesNotContain("HubDbOwner", text);
            Assert.DoesNotContain("\"FIELD_ENCRYPTION_KEY=", text);
        }
        Assert.Contains("\"FIELD_ENCRYPTION_KEY_FILE=/run/secrets/field_encryption_key\"", env);

        // Nor in the running process's environment.
        var (_, processEnv) = await stack.RunAsync("exec", "-T", "api", "sh", "-c", "tr '\\0' '\\n' < /proc/1/environ");
        Assert.DoesNotContain(stack.OwnerPassword, processEnv);
        Assert.DoesNotContain(stack.FieldKey, processEnv);
        Assert.Contains("FIELD_ENCRYPTION_KEY_FILE=", processEnv);

        // The migration job got the key the same way.
        var migrateEnv = await stack.InspectAsync("migrate", "{{json .Config.Env}}");
        Assert.DoesNotContain(stack.FieldKey, migrateEnv);
        Assert.Contains("FIELD_ENCRYPTION_KEY_FILE=/run/secrets/field_encryption_key", migrateEnv);
    }

    [ComposeFact]
    public async Task Field_key_is_read_from_the_secret_file()
    {
        // The secret is mounted as a file at the path the API is told about...
        var (exit, mounted) = await stack.RunAsync("exec", "-T", "api", "sh", "-c", "test -r \"$FIELD_ENCRYPTION_KEY_FILE\" && wc -c < \"$FIELD_ENCRYPTION_KEY_FILE\"");
        Assert.True(exit == 0, mounted);
        Assert.True(int.Parse(mounted.Trim(), System.Globalization.CultureInfo.InvariantCulture) >= 44);
        // ...and the seeded keys, encrypted by the migration job, decrypt with it in the API
        // (Ticket_flow_works_end_to_end_with_row_level_checks issues one); a stored key also
        // decrypts with the test's copy of the key, so both used the file's key.
        var (_, stored) = await stack.RunAsync("exec", "-T", "-u", "postgres", "db", "psql", "-d", "resourcehub", "-At", "-c",
            "SELECT \"Id\" || ' ' || \"KeyCiphertext\" FROM license_keys LIMIT 1");
        var parts = stored.Trim().Split(' ');
        var plain = ResourceHub.Api.Security.FieldEncryptor.FromBase64(stack.FieldKey).Decrypt(parts[1], Guid.Parse(parts[0]));
        Assert.StartsWith("JBIDE-", plain);
    }

    [ComposeFact]
    public async Task Api_container_runs_as_non_root_with_a_read_only_filesystem()
    {
        var (_, user) = await stack.RunAsync("exec", "-T", "api", "id", "-u");
        Assert.NotEqual("0", user.Trim());
        var (exit, _) = await stack.RunAsync("exec", "-T", "api", "sh", "-c", "touch /app/x");
        Assert.NotEqual(0, exit);
    }
}
