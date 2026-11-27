// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Device return tests; a return now has to show the device's check-in code as well as its token (T1) (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public sealed record IssuedDevice(string Token, string CheckInCode);

public class ReturnTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Returning_a_device_adds_one_to_stock_and_records_the_time()
    {
        var asset = await S.PhysicalAsync(units: 3, loanDays: 14);
        var devices = await AllocateDevicesAsync(asset, 2);
        factory.Clock.Advance(TimeSpan.FromHours(30));
        var returnedAt = factory.Clock.GetUtcNow();

        var response = await ReturnAsync(asset, devices[0]);

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        Assert.Equal(devices[0].Token, body.Str("token"));
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
        var devices = await AllocateDevicesAsync(asset, 1);
        await (await ReturnAsync(asset, devices[0])).AssertStatusAsync(HttpStatusCode.OK);

        var again = await ReturnAsync(asset, devices[0]);

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
        var device = Device(allocated, asset, 0);

        factory.Clock.Advance(TimeSpan.FromDays(2));

        var during = await S.GetAssetAsync(asset);
        Assert.Equal(1, during.GetProperty("availability").GetProperty("overdue").GetInt32());
        Assert.Equal(0, during.GetProperty("availability").GetProperty("checkedout").GetInt32());
        var overdue = await (await _client.GetAsync("/api/v1/assets?availability=overdue&pageSize=100", Ids.Admin)).JsonAsync();
        Assert.Contains(overdue.GetProperty("items").EnumerateArray(), a => a.Id() == asset);

        var returned = await (await ReturnAsync(asset, device)).JsonAsync();
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
        var devices = await AllocateDevicesAsync(asset, 1);

        await (await ReturnAsync(asset, devices[0] with { Token = "HW-NOPE" })).AssertStatusAsync(HttpStatusCode.NotFound);
        await (await ReturnAsync(other, devices[0])).AssertStatusAsync(HttpStatusCode.NotFound);
        await (await ReturnAsync(Guid.NewGuid(), devices[0])).AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Return_needs_a_token_and_a_physical_asset()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var missing = await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return", new { }, Ids.Admin);
        await missing.AssertStatusAsync(HttpStatusCode.BadRequest);

        var license = await ReturnAsync(Ids.AssetJetBrains, new IssuedDevice("HW-ANY", "CI-ANY"));
        await license.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Only_administrators_process_returns()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var devices = await AllocateDevicesAsync(asset, 1);

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return",
            new { token = devices[0].Token, checkInCode = devices[0].CheckInCode }, Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    // ---- T1: what a return must prove ----

    [DbFact]
    public async Task Token_alone_does_not_mark_a_device_returned()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var devices = await AllocateDevicesAsync(asset, 1);

        var noCode = await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return", new { token = devices[0].Token }, Ids.Admin);
        await noCode.AssertStatusAsync(HttpStatusCode.BadRequest);

        var wrongCode = await ReturnAsync(asset, devices[0] with { CheckInCode = "CI-00000000000000000000" });
        await wrongCode.AssertStatusAsync(HttpStatusCode.UnprocessableEntity);

        // The device is still out and the stock did not move.
        var after = await S.GetAssetAsync(asset);
        Assert.Equal(0, after.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(1, after.GetProperty("availability").GetProperty("checkedout").GetInt32());
        var stillOut = await factory.WithDbAsync(db => db.DeviceAssignments.SingleAsync(d => d.Token == devices[0].Token));
        Assert.Null(stillOut.ReturnedAt);
    }

    [DbFact]
    public async Task Check_in_code_of_another_device_is_refused()
    {
        var asset = await S.PhysicalAsync(units: 2);
        var devices = await AllocateDevicesAsync(asset, 2);

        var swapped = await ReturnAsync(asset, devices[0] with { CheckInCode = devices[1].CheckInCode });

        await swapped.AssertStatusAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(0, (await S.GetAssetAsync(asset)).GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Check_in_code_is_stored_only_as_a_hash_and_never_shown_to_the_borrower()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var devices = await AllocateDevicesAsync(asset, 1);

        var stored = await factory.WithDbAsync(db => db.DeviceAssignments.AsNoTracking().SingleAsync(d => d.Token == devices[0].Token));
        Assert.DoesNotContain(devices[0].CheckInCode, stored.CheckInCodeHash ?? "");
        Assert.Equal(64, stored.CheckInCodeHash?.Length);

        // The requester's allocation view shows the token but not the check-in code.
        var listing = await _client.GetAsync($"/api/v1/assets/allocations?courseId={Ids.CourseCs501}", Ids.InstructorChen);
        await listing.AssertStatusAsync(HttpStatusCode.OK);
        var text = await listing.Content.ReadAsStringAsync();
        Assert.Contains(devices[0].Token, text);
        Assert.DoesNotContain(devices[0].CheckInCode, text);
        Assert.DoesNotContain("checkInCode", text);
    }

    private Task<HttpResponseMessage> ReturnAsync(Guid asset, IssuedDevice device) =>
        _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return",
            new { token = device.Token, checkInCode = device.CheckInCode }, Ids.Admin);

    private async Task<List<IssuedDevice>> AllocateDevicesAsync(Guid asset, int quantity)
    {
        var result = await S.AllocatedTicketAsync(new { assetId = asset, quantity });
        return Enumerable.Range(0, quantity).Select(n => Device(result, asset, n)).ToList();
    }

    public static IssuedDevice Device(JsonElement allocation, Guid asset, int index)
    {
        var device = allocation.GetProperty("allocations").EnumerateArray()
            .Single(a => a.Id("assetId") == asset)
            .GetProperty("devices")[index];
        return new IssuedDevice(device.Str("token"), device.Str("checkInCode"));
    }
}
