// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Shared steps for ticket and allocation tests; reads now send an admin token (~100%)
// Human Modifications: None yet
// Verification: Used by the ticket, allocation, return and security tests (dotnet test against PostgreSQL 18)
// Confidence: High

using System.Net;
using System.Text.Json;

namespace ResourceHub.Tests.Infrastructure;

/// <summary>Common API steps, each asserting the expected status code.</summary>
public sealed class Scenario(HttpClient client)
{
    public HttpClient Client => client;

    public async Task<Guid> PhysicalAsync(int units = 3, int loanDays = 14, string? category = null)
    {
        return await CreateAssetAsync(new
        {
            name = "Test kit",
            type = "physical",
            category = category ?? "Test device",
            departmentProgram = "Computer Science",
            units,
            defaultLoanDays = loanDays,
        });
    }

    public async Task<Guid> LicenseAsync(params string[] keys)
    {
        return await CreateAssetAsync(new
        {
            name = "Test license",
            type = "license",
            category = "Test license",
            departmentProgram = "Computer Science",
            licenseKeys = keys,
        });
    }

    public async Task<Guid> VoucherAsync(decimal balance)
    {
        return await CreateAssetAsync(new
        {
            name = "Test credits",
            type = "voucher",
            category = "Test credits",
            departmentProgram = "Computer Science",
            balance,
        });
    }

    public async Task<Guid> CreateAssetAsync(object body)
    {
        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/assets", body, Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.Created);
        return (await response.JsonAsync()).Id();
    }

    /// <summary>A ticket from Dr. Chen for CS501 A1.</summary>
    public async Task<JsonElement> SubmitForCourseAsync(params object[] items) =>
        await SubmitAsync(Ids.InstructorChen, new { courseId = Ids.CourseCs501, items });

    public async Task<JsonElement> SubmitAsync(Guid requester, object body)
    {
        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", body, requester);
        await response.AssertStatusAsync(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    public async Task<HttpResponseMessage> SetStatusAsync(Guid ticketId, string status, Guid actor, string? reason = null) =>
        await client.SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{ticketId}", new { status, reason }, actor);

    public async Task ApproveAsync(Guid ticketId)
    {
        var response = await SetStatusAsync(ticketId, "approved", Ids.ItDirector);
        await response.AssertStatusAsync(HttpStatusCode.OK);
    }

    public async Task<HttpResponseMessage> AllocateAsync(Guid ticketId, Guid? actor = null) =>
        await client.SendAsync(HttpMethod.Post, $"/api/v1/assets/tickets/{ticketId}/allocate", user: actor ?? Ids.Admin);

    /// <summary>Submit, approve and allocate; returns the allocation response.</summary>
    public async Task<JsonElement> AllocatedTicketAsync(params object[] items)
    {
        var ticket = await SubmitForCourseAsync(items);
        await ApproveAsync(ticket.Id());
        var response = await AllocateAsync(ticket.Id());
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    public async Task<JsonElement> GetTicketAsync(Guid ticketId)
    {
        var response = await client.GetAsync($"/api/v1/assets/tickets/{ticketId}", Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    public async Task<JsonElement> GetAssetAsync(Guid assetId)
    {
        var response = await client.GetAsync($"/api/v1/assets/{assetId}", Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }
}
