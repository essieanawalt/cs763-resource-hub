// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Tests that the starting assets in spec section 7 are seeded with their fixed ids; reads now send a bearer token (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class SeedDataTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [DbFact]
    public async Task Raspberry_pi_kit_is_seeded_as_physical_with_10_units_and_14_day_loan()
    {
        var asset = await GetAsset(Ids.AssetRaspberryPi);

        Assert.Equal("Raspberry Pi 5 kit", asset.Str("name"));
        Assert.Equal("physical", asset.Str("type"));
        Assert.Equal("IoT node", asset.Str("category"));
        Assert.Equal("Electrical Engineering", asset.Str("departmentProgram"));
        Assert.Equal(10, asset.GetProperty("totalStock").GetDecimal());
        Assert.Equal(10, asset.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(14, asset.GetProperty("defaultLoanDays").GetInt32());
        var availability = asset.GetProperty("availability");
        Assert.Equal(10, availability.GetProperty("available").GetInt32());
        Assert.Equal(0, availability.GetProperty("reserved").GetInt32());
        Assert.Equal(0, availability.GetProperty("checkedout").GetInt32());
        Assert.Equal(0, availability.GetProperty("overdue").GetInt32());
    }

    [DbFact]
    public async Task Laptop_is_seeded_as_physical_with_5_units_and_30_day_loan()
    {
        var asset = await GetAsset(Ids.AssetLaptop);

        Assert.Equal("Dell Latitude 7450 laptop", asset.Str("name"));
        Assert.Equal("Laptop", asset.Str("category"));
        Assert.Equal("Computer Science", asset.Str("departmentProgram"));
        Assert.Equal(5, asset.GetProperty("totalStock").GetDecimal());
        Assert.Equal(30, asset.GetProperty("defaultLoanDays").GetInt32());
    }

    [DbFact]
    public async Task JetBrains_license_is_seeded_with_three_unissued_keys_that_are_not_exposed()
    {
        var asset = await GetAsset(Ids.AssetJetBrains);

        Assert.Equal("license", asset.Str("type"));
        Assert.Equal("IDE license", asset.Str("category"));
        Assert.Equal(3, asset.GetProperty("totalStock").GetDecimal());
        Assert.Equal(3, asset.GetProperty("availability").GetProperty("available").GetInt32());
        Assert.Equal(0, asset.GetProperty("availability").GetProperty("issued").GetInt32());
        // Unissued keys are secrets: the catalog shows counts, never the key values.
        Assert.DoesNotContain("JBIDE-", asset.GetRawText());
    }

    [DbFact]
    public async Task Cloud_credit_pool_is_seeded_as_voucher_with_balance_5000()
    {
        var asset = await GetAsset(Ids.AssetCloudCredits);

        Assert.Equal("voucher", asset.Str("type"));
        Assert.Equal("Cloud credits", asset.Str("category"));
        Assert.Equal(5000.00m, asset.GetProperty("totalStock").GetDecimal());
        Assert.Equal(5000.00m, asset.GetProperty("availability").GetProperty("balance").GetDecimal());
    }

    private async Task<System.Text.Json.JsonElement> GetAsset(Guid id)
    {
        var response = await _client.GetAsync($"/api/v1/assets/{id}", Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        var asset = await response.JsonAsync();
        Assert.Equal(id, asset.Id());
        return asset;
    }
}
