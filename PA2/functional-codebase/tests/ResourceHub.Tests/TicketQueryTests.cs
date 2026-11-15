// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for GET /api/v1/assets/tickets filters, paging and order (~100%)
// Human Modifications: None yet
// Verification: dotnet test (failed before the listing route existed, passes after)
// Confidence: High

using System.Net;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class TicketQueryTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Filter_by_asset_category_finds_tickets_requesting_that_category()
    {
        var category = "Cat-" + Guid.NewGuid().ToString("N")[..8];
        var asset = await S.PhysicalAsync(category: category);
        var match = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });
        await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var page = await ListAsync($"/api/v1/assets/tickets?assetCategory={Uri.EscapeDataString(category)}");

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(match.Id(), item.Id());
    }

    [DbFact]
    public async Task Filter_by_research_lab_and_project_combined()
    {
        var category = "Cat-" + Guid.NewGuid().ToString("N")[..8];
        var asset = await S.PhysicalAsync(category: category);
        var withProject = await S.SubmitAsync(Ids.AdvisorRaman, new
        {
            researchLabId = Ids.LabEmbedded, projectId = Ids.ProjectParking,
            items = new[] { new { assetId = asset, quantity = 1 } },
        });
        await S.SubmitAsync(Ids.AdvisorRaman, new
        {
            researchLabId = Ids.LabEmbedded,
            items = new[] { new { assetId = asset, quantity = 1 } },
        });

        var page = await ListAsync(
            $"/api/v1/assets/tickets?researchLabId={Ids.LabEmbedded}&projectId={Ids.ProjectParking}&assetCategory={Uri.EscapeDataString(category)}");

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(withProject.Id(), item.Id());
    }

    [DbFact]
    public async Task Filter_by_course_and_status_returns_only_matching_tickets()
    {
        await S.SubmitAsync(Ids.InstructorOkafor, new
        {
            courseId = Ids.CourseEe580,
            items = new[] { new { assetId = Ids.AssetRaspberryPi, quantity = 1 } },
        });

        var page = await ListAsync($"/api/v1/assets/tickets?courseId={Ids.CourseEe580}&status=pending&pageSize=100");

        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, t =>
        {
            Assert.Equal(Ids.CourseEe580, t.Id("courseId"));
            Assert.Equal("pending", t.Str("status"));
        });

        var none = await ListAsync($"/api/v1/assets/tickets?courseId={Ids.CourseEe580}&status=allocated");
        Assert.Equal(0, none.GetProperty("total").GetInt32());
    }

    [DbFact]
    public async Task Status_filter_accepts_several_values()
    {
        var page = await ListAsync("/api/v1/assets/tickets?status=pending,approved,allocated&pageSize=100");

        Assert.All(page.GetProperty("items").EnumerateArray(),
            t => Assert.Contains(t.Str("status"), new[] { "pending", "approved", "allocated" }));
    }

    [DbFact]
    public async Task Listing_is_paged_and_newest_first()
    {
        var category = "Cat-" + Guid.NewGuid().ToString("N")[..8];
        var asset = await S.PhysicalAsync(category: category);
        var first = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var third = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });

        var page1 = await ListAsync($"/api/v1/assets/tickets?assetCategory={Uri.EscapeDataString(category)}&pageSize=2");
        var page2 = await ListAsync($"/api/v1/assets/tickets?assetCategory={Uri.EscapeDataString(category)}&pageSize=2&page=2");

        Assert.Equal(3, page1.GetProperty("total").GetInt32());
        Assert.Equal([third.Id(), second.Id()], page1.GetProperty("items").EnumerateArray().Select(t => t.Id()).ToArray());
        Assert.Equal([first.Id()], page2.GetProperty("items").EnumerateArray().Select(t => t.Id()).ToArray());
    }

    [DbTheory]
    [InlineData("status=lost")]
    [InlineData("status=pending,lost")]
    [InlineData("courseId=not-a-guid")]
    [InlineData("pageSize=500")]
    public async Task Invalid_query_parameters_are_400(string query)
    {
        var response = await _client.GetAsync($"/api/v1/assets/tickets?{query}");
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    private async Task<JsonElement> ListAsync(string url)
    {
        var response = await _client.GetAsync(url);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }
}
