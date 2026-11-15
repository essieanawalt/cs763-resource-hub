// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the /api/v1/assets integration tests (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System.Net;
using System.Text.Json.Nodes;
using ResourceHub.Api.Data;

namespace ResourceHub.Tests;

public class AssetEndpointTests : IClassFixture<HubApiFactory>
{
    private readonly HubApiFactory api;

    public AssetEndpointTests(HubApiFactory api)
    {
        this.api = api;
    }

    [DatabaseFact]
    public async Task Seeded_assets_are_listed_with_their_fixed_ids()
    {
        var body = await (await api.As(MockData.Ids.Chen).GetAsync("/api/v1/assets?pageSize=100"))
            .ReadAsync(HttpStatusCode.OK);

        var ids = body["items"]!.AsArray().Select(a => a!["id"]!.GetValue<string>()).ToList();
        Assert.Contains(MockData.Ids.RaspberryPi.ToString(), ids);
        Assert.Contains(MockData.Ids.Laptop.ToString(), ids);
        Assert.Contains(MockData.Ids.JetBrains.ToString(), ids);
        Assert.Contains(MockData.Ids.CloudCredits.ToString(), ids);
    }

    [DatabaseFact]
    public async Task Post_creates_physical_asset_and_get_returns_it()
    {
        var category = Steps.NewCategory();
        var response = await api.As(MockData.Ids.Rivera).PostJson("/api/v1/assets", new
        {
            name = "Arduino Uno", type = "physical", category, departmentProgram = "Electrical Engineering",
            totalUnits = 8, defaultLoanDays = 21,
        });
        var created = await response.ReadAsync(HttpStatusCode.Created);
        Assert.NotNull(response.Headers.Location);

        var fetched = await (await api.As(MockData.Ids.Chen).GetAsync(response.Headers.Location))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Equal(created["id"]!.GetValue<string>(), fetched["id"]!.GetValue<string>());
        Assert.Equal("physical", fetched["type"]!.GetValue<string>());
        Assert.Equal(8m, fetched["totalStock"]!.GetValue<decimal>());
        Assert.Equal(8, fetched["physical"]!["available"]!.GetValue<int>());
        Assert.Equal(21, fetched["defaultLoanDays"]!.GetValue<int>());
    }

    [DatabaseFact]
    public async Task Get_list_filters_by_category_department_and_availability()
    {
        var client = api.As(MockData.Ids.Ferreira);

        var byCategory = await (await client.GetAsync("/api/v1/assets?category=iot%20node")).ReadAsync(HttpStatusCode.OK);
        Assert.Equal(MockData.Ids.RaspberryPi.ToString(), Single(byCategory)["id"]!.GetValue<string>());

        var combined = await (await client.GetAsync(
                "/api/v1/assets?departmentProgram=Computer%20Science&category=Laptop&availability=available"))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Equal(MockData.Ids.Laptop.ToString(), Single(combined)["id"]!.GetValue<string>());

        // A drained asset no longer counts as available.
        var category = Steps.NewCategory();
        await Steps.CreatePhysicalAsync(api, category, units: 0);
        var empty = await (await client.GetAsync($"/api/v1/assets?category={category}&availability=available"))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Empty(empty["items"]!.AsArray());
    }

    [DatabaseFact]
    public async Task Put_updates_asset_and_remaining_stock_moves_with_total()
    {
        var category = Steps.NewCategory();
        var assetId = await Steps.CreatePhysicalAsync(api, category, units: 4);

        var updated = await (await api.As(MockData.Ids.Rivera).PutJson($"/api/v1/assets/{assetId}", new
        {
            name = "Renamed device", category, departmentProgram = "Electrical Engineering",
            totalUnits = 6, defaultLoanDays = 7,
        })).ReadAsync(HttpStatusCode.OK);

        Assert.Equal("Renamed device", updated["name"]!.GetValue<string>());
        Assert.Equal(6m, updated["totalStock"]!.GetValue<decimal>());
        Assert.Equal(6m, updated["remainingStock"]!.GetValue<decimal>());
        Assert.Equal(7, updated["defaultLoanDays"]!.GetValue<int>());
    }

    [DatabaseFact]
    public async Task Delete_removes_unused_asset()
    {
        var assetId = await Steps.CreateVoucherAsync(api, Steps.NewCategory(), 100m);
        var admin = api.As(MockData.Ids.Rivera);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/assets/{assetId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/assets/{assetId}")).StatusCode);
    }

    [DatabaseFact]
    public async Task License_keys_are_masked_in_the_catalog()
    {
        var response = await api.As(MockData.Ids.Rivera).GetAsync($"/api/v1/assets/{MockData.Ids.JetBrains}");
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(raw)!;

        Assert.DoesNotContain("JBIDE-7K2M-Q9XA", raw);
        Assert.Equal(3, body["licenseKeys"]!.AsArray().Count);
        Assert.Contains(body["licenseKeys"]!.AsArray(), k => k!["maskedValue"]!.GetValue<string>() == "****Q9XA");
    }

    [DatabaseFact]
    public async Task Asset_changes_need_an_administrator()
    {
        var body = new { name = "x", type = "voucher", category = "x", departmentProgram = "x", totalCredit = 1 };

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.As(null).PostJson("/api/v1/assets", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.As(MockData.Ids.Chen).PostJson("/api/v1/assets", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.As(Guid.NewGuid()).GetAsync("/api/v1/assets")).StatusCode);
    }

    [DatabaseFact]
    public async Task Post_rejects_fields_that_do_not_match_the_type()
    {
        var response = await api.As(MockData.Ids.Rivera).PostJson("/api/v1/assets", new
        {
            name = "Bad", type = "voucher", category = "x", departmentProgram = "x", totalCredit = 10, totalUnits = 3,
        });
        await response.ReadAsync(HttpStatusCode.BadRequest);
    }

    private static JsonNode Single(JsonNode page) => Assert.Single(page["items"]!.AsArray())!;
}
