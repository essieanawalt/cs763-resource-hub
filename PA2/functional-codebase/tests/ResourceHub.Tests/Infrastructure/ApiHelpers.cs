// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Mock data ids from spec section 7 and HTTP helpers for the tests (~100%)
// Human Modifications: None yet
// Verification: Used by every test in ResourceHub.Tests
// Confidence: High

using System.Net.Http.Json;
using System.Text.Json;

namespace ResourceHub.Tests.Infrastructure;

/// <summary>Fixed ids from spec section 7.</summary>
public static class Ids
{
    public static readonly Guid CourseCs501 = Guid.Parse("10000000-0000-4000-8000-000000000001");
    public static readonly Guid CourseEe580 = Guid.Parse("10000000-0000-4000-8000-000000000002");
    public static readonly Guid LabDataScience = Guid.Parse("20000000-0000-4000-8000-000000000001");
    public static readonly Guid LabEmbedded = Guid.Parse("20000000-0000-4000-8000-000000000002");
    public static readonly Guid ProjectDashboard = Guid.Parse("30000000-0000-4000-8000-000000000001");
    public static readonly Guid ProjectParking = Guid.Parse("30000000-0000-4000-8000-000000000002");
    public static readonly Guid InstructorChen = Guid.Parse("40000000-0000-4000-8000-000000000001");
    public static readonly Guid InstructorOkafor = Guid.Parse("40000000-0000-4000-8000-000000000002");
    public static readonly Guid AdvisorFerreira = Guid.Parse("40000000-0000-4000-8000-000000000003");
    public static readonly Guid AdvisorRaman = Guid.Parse("40000000-0000-4000-8000-000000000004");
    public static readonly Guid ItDirector = Guid.Parse("40000000-0000-4000-8000-000000000005");
    public static readonly Guid Admin = Guid.Parse("40000000-0000-4000-8000-000000000006");
    public static readonly Guid AssetRaspberryPi = Guid.Parse("50000000-0000-4000-8000-000000000001");
    public static readonly Guid AssetLaptop = Guid.Parse("50000000-0000-4000-8000-000000000002");
    public static readonly Guid AssetJetBrains = Guid.Parse("50000000-0000-4000-8000-000000000003");
    public static readonly Guid AssetCloudCredits = Guid.Parse("50000000-0000-4000-8000-000000000004");
}

public static class ApiHelpers
{
    public const string UserHeader = "X-User-Id";

    public static async Task<HttpResponseMessage> SendAsync(
        this HttpClient client, HttpMethod method, string url, object? body = null, Guid? user = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is string raw)
        {
            // Raw JSON text, for malformed or hand-written bodies.
            request.Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json");
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user.Value.ToString());
        }
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), $"Expected a JSON body, got an empty one ({(int)response.StatusCode}).");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    public static async Task AssertStatusAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
    }

    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;

    public static Guid Id(this JsonElement e, string name = "id") => e.GetProperty(name).GetGuid();
}
