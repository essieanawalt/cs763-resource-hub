// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Static checks of the Compose and settings files (CWE-1392/1327/250/260) and live tests against the Compose stack with a test JWKS (~100%)
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

    [Fact]
    public void No_password_has_a_committed_fallback()
    {
        Assert.DoesNotContain("resourcehub_dev", Compose);
        foreach (var variable in new[] { "POSTGRES_PASSWORD", "APP_DB_PASSWORD", "FIELD_ENCRYPTION_KEY", "JWKS_HOST_PATH" })
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
        var init = WorkingFolder.Read(System.IO.Path.Combine("db", "init", "01-app-role.sh"));
        Assert.Contains("NOSUPERUSER", init);
        Assert.Contains("NOCREATEROLE", init);
        Assert.Contains("NOCREATEDB", init);
    }

    [Fact]
    public void Jwks_is_mounted_read_only_and_api_is_bound_to_localhost()
    {
        var api = Service("api");
        Assert.Contains("read_only: true", api);
        Assert.Contains("JWT_JWKS_PATH", api);
        Assert.Contains("JWT_ISSUER", api);
        Assert.Contains("JWT_AUDIENCE", api);
        Assert.Contains("FIELD_ENCRYPTION_KEY", api);
        Assert.Contains("127.0.0.1:", api);
    }

    [Fact]
    public void Env_example_lists_every_variable_with_placeholders_only()
    {
        var example = WorkingFolder.Read(".env.example");
        foreach (var variable in new[] { "POSTGRES_PASSWORD", "APP_DB_USER", "APP_DB_PASSWORD", "FIELD_ENCRYPTION_KEY", "JWKS_HOST_PATH", "JWT_JWKS_PATH", "JWT_ISSUER", "JWT_AUDIENCE", "API_PORT" })
        {
            Assert.Contains(variable + "=", example);
        }
        var key = example.Split('\n').Single(l => l.StartsWith("FIELD_ENCRYPTION_KEY=", StringComparison.Ordinal))["FIELD_ENCRYPTION_KEY=".Length..].Trim();
        Assert.ThrowsAny<Exception>(() => ResourceHub.Api.Security.FieldEncryptor.FromBase64(key));
        Assert.Contains(".env", WorkingFolder.Read(".gitignore").Split('\n').Select(l => l.Trim()));
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

    public HttpClient Client { get; } = new();
    public string FieldKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

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
        _env["JWKS_HOST_PATH"] = jwks;
        _env["FIELD_ENCRYPTION_KEY"] = FieldKey;
        _env["POSTGRES_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _env["APP_DB_USER"] = "resourcehub_app";
        _env["APP_DB_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
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
                var logs = await RunAsync("logs", "--no-color", "api");
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
            "SELECT rolsuper::text || ',' || rolcreaterole::text || ',' || rolcreatedb::text FROM pg_roles WHERE rolname = 'resourcehub_app'");
        Assert.Equal(0, exit);
        Assert.Equal("false,false,false", output.Trim());
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
