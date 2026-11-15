// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the test factory (throwaway database per class), test clock and HTTP helpers (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ResourceHub.Api.Data;

namespace ResourceHub.Tests;

// Runs the API against its own freshly migrated PostgreSQL database, dropped afterwards.
// Needs RESOURCEHUB_TEST_DB; tests using it are [DatabaseFact] and skip without it.
public sealed class HubApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string? connectionString;

    public HubApiFactory()
    {
        if (DatabaseFactAttribute.ConnectionString is { } baseConnection)
        {
            var csb = new NpgsqlConnectionStringBuilder(baseConnection)
            {
                Database = $"resourcehub_test_{Guid.NewGuid():N}",
            };
            connectionString = csb.ConnectionString;
        }
    }

    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HubDb", connectionString ?? "Host=127.0.0.1;Port=1");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        if (connectionString is null)
        {
            return;
        }
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HubDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (connectionString is not null)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<HubDbContext>().Database.EnsureDeletedAsync();
        }
        await DisposeAsync();
    }

    // A client acting as one of the mock users (or anonymous when userId is null).
    public HttpClient As(Guid? userId)
    {
        var client = CreateClient();
        if (userId is { } id)
        {
            client.DefaultRequestHeaders.Add("X-User-Id", id.ToString());
        }
        return client;
    }
}

// Fixed, movable time so due dates and overdue rules can be tested.
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now = now.Add(by);
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static async Task<JsonNode> ReadAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, Options);

    public static Task<HttpResponseMessage> PutJson(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, Options);
}

// Shared steps for building test data through the API itself.
public static class Steps
{
    public static string NewCategory() => $"test-{Guid.NewGuid():N}";

    public static async Task<Guid> CreatePhysicalAsync(HubApiFactory api, string category, int units, int loanDays = 14)
    {
        var response = await api.As(MockData.Ids.Rivera).PostJson("/api/v1/assets", new
        {
            name = "Test device", type = "physical", category, departmentProgram = "Computer Science",
            totalUnits = units, defaultLoanDays = loanDays,
        });
        return Guid.Parse((await response.ReadAsync(HttpStatusCode.Created))["id"]!.GetValue<string>());
    }

    public static async Task<Guid> CreateLicenseAsync(HubApiFactory api, string category, params string[] keys)
    {
        var response = await api.As(MockData.Ids.Rivera).PostJson("/api/v1/assets", new
        {
            name = "Test license", type = "license", category, departmentProgram = "Computer Science",
            licenseKeys = keys,
        });
        return Guid.Parse((await response.ReadAsync(HttpStatusCode.Created))["id"]!.GetValue<string>());
    }

    public static async Task<Guid> CreateVoucherAsync(HubApiFactory api, string category, decimal credit)
    {
        var response = await api.As(MockData.Ids.Rivera).PostJson("/api/v1/assets", new
        {
            name = "Test credits", type = "voucher", category, departmentProgram = "Computer Science",
            totalCredit = credit,
        });
        return Guid.Parse((await response.ReadAsync(HttpStatusCode.Created))["id"]!.GetValue<string>());
    }

    // Dr. Chen (CS501 instructor) submits a ticket for her course.
    public static async Task<Guid> SubmitAsync(HubApiFactory api, params object[] items)
    {
        var response = await api.As(MockData.Ids.Chen).PostJson("/api/v1/assets/tickets", new
        {
            courseId = MockData.Ids.CourseCs501,
            projectId = MockData.Ids.ProjectDashboard,
            notes = "Sprint 3 hardware",
            items,
        });
        return Guid.Parse((await response.ReadAsync(HttpStatusCode.Created))["id"]!.GetValue<string>());
    }

    public static async Task ApproveAsync(HubApiFactory api, Guid ticketId)
    {
        var response = await api.As(MockData.Ids.Blake)
            .PutJson($"/api/v1/assets/tickets/{ticketId}", new { status = "approved", comment = "OK" });
        await response.ReadAsync(HttpStatusCode.OK);
    }

    public static async Task<JsonNode> GetAssetAsync(HubApiFactory api, Guid assetId) =>
        await (await api.As(MockData.Ids.Rivera).GetAsync($"/api/v1/assets/{assetId}")).ReadAsync(HttpStatusCode.OK);
}
