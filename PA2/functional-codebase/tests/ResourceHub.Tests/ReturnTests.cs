// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for POST /api/v1/assets/{id}/return and overdue tracking (~100%)
// Human Modifications: None yet
// Verification: dotnet test (failed before the return route existed, passes after)
// Confidence: High

using System.Net;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class ReturnTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Returning_a_device_adds_one_to_stock_and_records_the_time()
    {
        var asset = await S.PhysicalAsync(units: 3, loanDays: 14);
        var tokens = await AllocateDevicesAsync(asset, 2);
        factory.Clock.Advance(TimeSpan.FromHours(30));
        var returnedAt = factory.Clock.GetUtcNow();

        var response = await ReturnAsync(asset, tokens[0]);

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        Assert.Equal(tokens[0], body.Str("token"));
        Assert.Equal(returnedAt, body.GetProperty("returnedAt").GetDateTimeOffset());
        Assert.Equal(30, body.GetProperty("checkoutDurationHours").GetDouble());
        Assert.False(body.GetProperty("wasOverdue").GetBoolean());
        Assert.Equal(2, body.GetProperty("remainingStock").GetInt32());

        var after = await S.GetAssetAsync(asset);
        Assert.Equal(2, after.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(1, after.GetProperty("availability").GetProperty("checkedout").GetInt32());
    }

    [DbFact]
    public async Task Returning_the_same_device_twice_is_409()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var tokens = await AllocateDevicesAsync(asset, 1);
        await (await ReturnAsync(asset, tokens[0])).AssertStatusAsync(HttpStatusCode.OK);

        var again = await ReturnAsync(asset, tokens[0]);

        await again.AssertStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal(1, (await S.GetAssetAsync(asset)).GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Device_past_its_due_date_is_overdue_until_returned()
    {
        var asset = await S.PhysicalAsync(units: 2, loanDays: 14);
        var ticket = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1, loanDays = 1 });
        await S.ApproveAsync(ticket.Id());
        var allocated = await (await S.AllocateAsync(ticket.Id())).JsonAsync();
        var token = Token(allocated, asset, 0);

        factory.Clock.Advance(TimeSpan.FromDays(2));

        var during = await S.GetAssetAsync(asset);
        Assert.Equal(1, during.GetProperty("availability").GetProperty("overdue").GetInt32());
        Assert.Equal(0, during.GetProperty("availability").GetProperty("checkedout").GetInt32());
        var overdue = await (await _client.GetAsync("/api/v1/assets?availability=overdue&pageSize=100")).JsonAsync();
        Assert.Contains(overdue.GetProperty("items").EnumerateArray(), a => a.Id() == asset);

        var returned = await (await ReturnAsync(asset, token)).JsonAsync();
        Assert.True(returned.GetProperty("wasOverdue").GetBoolean());

        var after = await S.GetAssetAsync(asset);
        Assert.Equal(0, after.GetProperty("availability").GetProperty("overdue").GetInt32());
        Assert.Equal(2, after.GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Unknown_token_or_token_of_another_asset_is_404()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var other = await S.PhysicalAsync(units: 1);
        var tokens = await AllocateDevicesAsync(asset, 1);

        await (await ReturnAsync(asset, "HW-NOPE")).AssertStatusAsync(HttpStatusCode.NotFound);
        await (await ReturnAsync(other, tokens[0])).AssertStatusAsync(HttpStatusCode.NotFound);
        await (await ReturnAsync(Guid.NewGuid(), tokens[0])).AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Return_needs_a_token_and_a_physical_asset()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var missing = await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return", new { }, Ids.Admin);
        await missing.AssertStatusAsync(HttpStatusCode.BadRequest);

        var license = await ReturnAsync(Ids.AssetJetBrains, "HW-ANY");
        await license.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Only_administrators_process_returns()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var tokens = await AllocateDevicesAsync(asset, 1);

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return", new { token = tokens[0] }, Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    private Task<HttpResponseMessage> ReturnAsync(Guid asset, string token) =>
        _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return", new { token }, Ids.Admin);

    private async Task<List<string>> AllocateDevicesAsync(Guid asset, int quantity)
    {
        var result = await S.AllocatedTicketAsync(new { assetId = asset, quantity });
        return Enumerable.Range(0, quantity).Select(n => Token(result, asset, n)).ToList();
    }

    private static string Token(JsonElement allocation, Guid asset, int index) =>
        allocation.GetProperty("allocations").EnumerateArray()
            .Single(a => a.Id("assetId") == asset)
            .GetProperty("devices")[index].Str("token");
}
