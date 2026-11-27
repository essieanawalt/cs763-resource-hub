// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: The Compose health check (dotnet ResourceHub.Api.dll --healthcheck): GET /api/v1/health on loopback, exit 0 only on 200, so the runtime image needs no curl or other extra package (~100%)
// Human Modifications: None yet
// Verification: HealthProbeTests, HealthTests, HealthUnreachableDbTests; ComposeStackTests.Api_container_reports_healthy
// Confidence: High

namespace ResourceHub.Api.Endpoints;

/// <summary>
/// Runs before the web host is built and reads no configuration, so it needs no database, JWKS or
/// key. It calls the public health endpoint only and sends no token.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--healthcheck";
    public const string Path = "/api/v1/health";
    private const int DefaultPort = 8080;
    private static readonly char[] PortSeparators = [';', ','];

    /// <summary>Exit code for the container health check: 0 healthy, 1 not.</summary>
    public static async Task<int> RunAsync(string? httpPorts)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        client.BaseAddress = new Uri(UrlFor(httpPorts).GetLeftPart(UriPartial.Authority));
        return await CheckAsync(client, CancellationToken.None);
    }

    public static async Task<int> CheckAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(Path, ct);
            return response.StatusCode == System.Net.HttpStatusCode.OK ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return 1;
        }
    }

    /// <summary>The first port in ASPNETCORE_HTTP_PORTS ("8080" or "8080;8081"), on 127.0.0.1.</summary>
    public static Uri UrlFor(string? httpPorts)
    {
        var first = (httpPorts ?? "").Split(PortSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var port = int.TryParse(first, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) && p is > 0 and <= 65535
            ? p
            : DefaultPort;
        return new Uri($"http://127.0.0.1:{port}{Path}");
    }
}
