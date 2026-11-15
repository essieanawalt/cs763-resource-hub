// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the GET /api/v1/assets/allocations endpoint (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using Microsoft.AspNetCore.Mvc;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Controllers;

[Route("api/v1/assets/allocations")]
// Classic constructor: Semgrep's C# parser cannot parse primary constructors.
public class AllocationsController : HubControllerBase
{
    private readonly AllocationService allocations;

    public AllocationsController(AllocationService allocations)
    {
        this.allocations = allocations;
    }

    // Needs exactly one of courseId or researchLabId.
    [HttpGet]
    public async Task<ActionResult<ActiveAllocationsResponse>> Get([FromQuery] AllocationQuery query, CancellationToken ct)
    {
        var result = await allocations.ActiveAsync(CurrentCaller, query, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }
}
