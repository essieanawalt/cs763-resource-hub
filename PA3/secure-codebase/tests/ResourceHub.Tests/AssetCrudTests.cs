// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for POST/GET/PUT/DELETE on /api/v1/assets, validation and role checks (~100%)
// Human Modifications: None (review only)
// Verification: dotnet test (failed before the create/update/delete routes existed, passes after); reviewed by me 2026-11-15
// Confidence: High

using System.Net;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class AssetCrudTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [DbFact]
    public async Task Admin_creates_physical_asset_and_reads_it_back()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", new
        {
            name = "Arduino Uno R4",
            type = "physical",
            category = "IoT node",
            departmentProgram = "Electrical Engineering",
            units = 8,
            defaultLoanDays = 21,
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        Assert.Equal($"/api/v1/assets/{created.Id()}", response.Headers.Location?.OriginalString);
        Assert.Equal("physical", created.Str("type"));
        Assert.Equal(8, created.GetProperty("totalStock").GetDecimal());
        Assert.Equal(8, created.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(21, created.GetProperty("defaultLoanDays").GetInt32());
        Assert.Equal(8, created.GetProperty("availability").GetProperty("available").GetInt32());

        var fetched = await (await _client.GetAsync($"/api/v1/assets/{created.Id()}")).JsonAsync();
        Assert.Equal("Arduino Uno R4", fetched.Str("name"));
    }

    [DbFact]
    public async Task Admin_creates_license_asset_and_keys_are_counted_not_shown()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", new
        {
            name = "MATLAB campus license",
            type = "license",
            category = "Analysis license",
            departmentProgram = "Electrical Engineering",
            licenseKeys = new[] { "MATLAB-AAAA-1111", "MATLAB-BBBB-2222" },
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        Assert.Equal(2, created.GetProperty("totalStock").GetDecimal());
        Assert.Equal(2, created.GetProperty("availability").GetProperty("available").GetInt32());
        Assert.DoesNotContain("MATLAB-AAAA", created.GetRawText());
    }

    [DbFact]
    public async Task Admin_creates_voucher_asset_with_balance()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", new
        {
            name = "AWS Educate credits",
            type = "voucher",
            category = "Cloud credits",
            departmentProgram = "Computer Science",
            balance = 1250.50m,
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        Assert.Equal(1250.50m, created.GetProperty("totalStock").GetDecimal());
        Assert.Equal(1250.50m, created.GetProperty("availability").GetProperty("balance").GetDecimal());
    }

    [DbFact]
    public async Task Create_without_user_header_is_401()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", ValidPhysical());
        await response.AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [DbFact]
    public async Task Create_with_unknown_user_is_401()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", ValidPhysical(), Guid.NewGuid());
        await response.AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [DbFact]
    public async Task Create_by_non_administrator_is_403()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", ValidPhysical(), Ids.InstructorChen);
        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbTheory]
    [InlineData("""{"type":"physical","category":"Laptop","departmentProgram":"CS","units":1,"defaultLoanDays":7}""")]
    [InlineData("""{"name":"X","category":"Laptop","departmentProgram":"CS","units":1,"defaultLoanDays":7}""")]
    [InlineData("""{"name":"X","type":"spaceship","category":"Laptop","departmentProgram":"CS","units":1}""")]
    [InlineData("""{"name":"X","type":"physical","category":"Laptop","departmentProgram":"CS","units":0,"defaultLoanDays":7}""")]
    [InlineData("""{"name":"X","type":"physical","category":"Laptop","departmentProgram":"CS","units":2}""")]
    [InlineData("""{"name":"X","type":"physical","category":"Laptop","departmentProgram":"CS","units":2,"defaultLoanDays":7,"balance":5}""")]
    [InlineData("""{"name":"X","type":"license","category":"IDE","departmentProgram":"CS"}""")]
    [InlineData("""{"name":"X","type":"license","category":"IDE","departmentProgram":"CS","licenseKeys":["A-1","A-1"]}""")]
    [InlineData("""{"name":"X","type":"license","category":"IDE","departmentProgram":"CS","licenseKeys":["bad key!"]}""")]
    [InlineData("""{"name":"X","type":"voucher","category":"Cloud","departmentProgram":"CS","balance":-5}""")]
    [InlineData("""{"name":"X","type":"voucher","category":"Cloud","departmentProgram":"CS","balance":1.005}""")]
    [InlineData("""{"name":"X","type":"voucher","category":"Cloud","departmentProgram":"CS","balance":10,"units":3}""")]
    [InlineData("""{"name":"   ","type":"voucher","category":"Cloud","departmentProgram":"CS","balance":10}""")]
    [InlineData("""not json""")]
    public async Task Create_with_invalid_body_is_400(string body)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", body, Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Get_unknown_asset_is_404()
    {
        var response = await _client.GetAsync($"/api/v1/assets/{Guid.NewGuid()}");
        await response.AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Admin_updates_physical_asset_and_remaining_stock_follows_total()
    {
        var id = await CreateAsync(ValidPhysical());

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{id}", new
        {
            name = "Renamed kit",
            type = "physical",
            category = "Sensor kit",
            departmentProgram = "Computer Science",
            units = 12,
            defaultLoanDays = 10,
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var updated = await response.JsonAsync();
        Assert.Equal("Renamed kit", updated.Str("name"));
        Assert.Equal("Sensor kit", updated.Str("category"));
        Assert.Equal(12, updated.GetProperty("totalStock").GetDecimal());
        Assert.Equal(12, updated.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(10, updated.GetProperty("defaultLoanDays").GetInt32());
    }

    [DbFact]
    public async Task Admin_adds_a_license_key_by_update()
    {
        var created = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", new
        {
            name = "Figma seats",
            type = "license",
            category = "Design license",
            departmentProgram = "Computer Science",
            licenseKeys = new[] { "FIGMA-0001" },
        }, Ids.Admin);
        var id = (await created.JsonAsync()).Id();

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{id}", new
        {
            name = "Figma seats",
            type = "license",
            category = "Design license",
            departmentProgram = "Computer Science",
            licenseKeys = new[] { "FIGMA-0001", "FIGMA-0002" },
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var updated = await response.JsonAsync();
        Assert.Equal(2, updated.GetProperty("totalStock").GetDecimal());
        Assert.Equal(2, updated.GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Update_cannot_change_asset_type()
    {
        var id = await CreateAsync(ValidPhysical());

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{id}", new
        {
            name = "X",
            type = "voucher",
            category = "Cloud",
            departmentProgram = "CS",
            balance = 10,
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Update_by_non_administrator_is_403_and_unknown_asset_is_404()
    {
        var id = await CreateAsync(ValidPhysical());

        var forbidden = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{id}", ValidPhysical(), Ids.ItDirector);
        await forbidden.AssertStatusAsync(HttpStatusCode.Forbidden);

        var missing = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{Guid.NewGuid()}", ValidPhysical(), Ids.Admin);
        await missing.AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Admin_deletes_asset_then_it_is_gone()
    {
        var id = await CreateAsync(ValidPhysical());

        var forbidden = await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/{id}", user: Ids.AdvisorRaman);
        await forbidden.AssertStatusAsync(HttpStatusCode.Forbidden);

        var deleted = await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/{id}", user: Ids.Admin);
        await deleted.AssertStatusAsync(HttpStatusCode.NoContent);

        var fetched = await _client.GetAsync($"/api/v1/assets/{id}");
        await fetched.AssertStatusAsync(HttpStatusCode.NotFound);

        var again = await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/{id}", user: Ids.Admin);
        await again.AssertStatusAsync(HttpStatusCode.NotFound);
    }

    private static object ValidPhysical() => new
    {
        name = "ESP32 dev board",
        type = "physical",
        category = "IoT node",
        departmentProgram = "Electrical Engineering",
        units = 4,
        defaultLoanDays = 7,
    };

    private async Task<Guid> CreateAsync(object body)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", body, Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).Id();
    }
}
