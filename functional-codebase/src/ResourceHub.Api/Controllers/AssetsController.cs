// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the /api/v1/assets endpoints, including device return (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Controllers;

[Route("api/v1/assets")]
// Classic constructor: Semgrep's C# parser cannot parse primary constructors.
public class AssetsController : HubControllerBase
{
    private readonly AssetService assets;
    private readonly AllocationService allocations;

    public AssetsController(AssetService assets, AllocationService allocations)
    {
        this.assets = assets;
        this.allocations = allocations;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<ActionResult<AssetResponse>> Create(CreateAssetRequest request, CancellationToken ct)
    {
        var result = await assets.CreateAsync(request, ct);
        return result.Error is { } error
            ? Problem(error)
            : CreatedAtAction(nameof(Get), new { assetId = result.Value!.Id }, result.Value);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<AssetResponse>>> List([FromQuery] AssetQuery query, CancellationToken ct) =>
        await assets.ListAsync(query, ct);

    [HttpGet("{assetId:guid}")]
    public async Task<ActionResult<AssetResponse>> Get(Guid assetId, CancellationToken ct)
    {
        var result = await assets.GetAsync(assetId, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }

    [HttpPut("{assetId:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<ActionResult<AssetResponse>> Update(Guid assetId, UpdateAssetRequest request, CancellationToken ct)
    {
        var result = await assets.UpdateAsync(assetId, request, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }

    [HttpDelete("{assetId:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<IActionResult> Delete(Guid assetId, CancellationToken ct)
    {
        var result = await assets.DeleteAsync(assetId, ct);
        return result.Error is { } error ? Problem(error) : NoContent();
    }

    [HttpPost("{assetId:guid}/return")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<ActionResult<ReturnDeviceResponse>> Return(Guid assetId, ReturnDeviceRequest request, CancellationToken ct)
    {
        var result = await allocations.ReturnAsync(CurrentCaller, assetId, request.Token, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }
}
