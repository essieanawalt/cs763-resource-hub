// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for GET /api/v1/assets filters, paging and sort order (~100%)
// Human Modifications: None yet
// Verification: dotnet test (failed before the listing route existed, passes after)
// Confidence: High

using System.Net;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class AssetQueryTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [DbFact]
    public async Task Filter_by_category_finds_the_seeded_laptop()
    {
        var page = await ListAsync("/api/v1/assets?category=Laptop");

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(Ids.AssetLaptop, item.Id());
    }

    [DbFact]
    public async Task Category_filter_ignores_case()
    {
        var page = await ListAsync("/api/v1/assets?category=iot%20NODE");

        Assert.Contains(page.GetProperty("items").EnumerateArray(), a => a.Id() == Ids.AssetRaspberryPi);
    }

    [DbFact]
    public async Task Filters_combine_category_department_and_availability()
    {
        var tag = Unique();
        var match = await CreatePhysical(tag, "Dept " + tag);
        await CreatePhysical(tag, "Other " + tag);                       // wrong department
        await CreatePhysical("Other " + tag, "Dept " + tag);             // wrong category

        var page = await ListAsync($"/api/v1/assets?category={Uri.EscapeDataString(tag)}&departmentProgram={Uri.EscapeDataString("Dept " + tag)}&availability=available");

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(match, item.Id());
        Assert.Equal(1, page.GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Filter_by_type_returns_only_that_type()
    {
        var page = await ListAsync("/api/v1/assets?type=voucher&pageSize=100");

        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, a => Assert.Equal("voucher", a.Str("type")));
        Assert.Contains(items, a => a.Id() == Ids.AssetCloudCredits);
    }

    [DbFact]
    public async Task Listing_is_paged_and_newest_first()
    {
        var tag = Unique();
        var first = await CreatePhysical(tag, "Paging");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await CreatePhysical(tag, "Paging");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var third = await CreatePhysical(tag, "Paging");

        var page1 = await ListAsync($"/api/v1/assets?category={Uri.EscapeDataString(tag)}&pageSize=2");
        var page2 = await ListAsync($"/api/v1/assets?category={Uri.EscapeDataString(tag)}&pageSize=2&page=2");

        Assert.Equal(3, page1.GetProperty("total").GetInt32());
        Assert.Equal(1, page1.GetProperty("page").GetInt32());
        Assert.Equal(2, page1.GetProperty("pageSize").GetInt32());
        Assert.Equal([third, second], page1.GetProperty("items").EnumerateArray().Select(a => a.Id()).ToArray());
        Assert.Equal([first], page2.GetProperty("items").EnumerateArray().Select(a => a.Id()).ToArray());
    }

    [DbFact]
    public async Task Default_listing_returns_seeded_assets()
    {
        var page = await ListAsync("/api/v1/assets?pageSize=100");

        var ids = page.GetProperty("items").EnumerateArray().Select(a => a.Id()).ToList();
        Assert.Contains(Ids.AssetRaspberryPi, ids);
        Assert.Contains(Ids.AssetLaptop, ids);
        Assert.Contains(Ids.AssetJetBrains, ids);
        Assert.Contains(Ids.AssetCloudCredits, ids);
        Assert.DoesNotContain("JBIDE-", page.GetRawText());
    }

    [DbTheory]
    [InlineData("availability=sometimes")]
    [InlineData("type=spaceship")]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Invalid_query_parameters_are_400(string query)
    {
        var response = await _client.GetAsync($"/api/v1/assets?{query}");
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    private static string Unique() => "Cat-" + Guid.NewGuid().ToString("N")[..8];

    private async Task<JsonElement> ListAsync(string url)
    {
        var response = await _client.GetAsync(url);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    private async Task<Guid> CreatePhysical(string category, string department)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", new
        {
            name = "Test device", type = "physical", category, departmentProgram = department, units = 2, defaultLoanDays = 7,
        }, Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).Id();
    }
}
