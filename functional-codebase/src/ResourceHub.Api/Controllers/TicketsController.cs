// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the /api/v1/assets/tickets endpoints, including allocate (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Controllers;

[Route("api/v1/assets/tickets")]
// Classic constructor: Semgrep's C# parser cannot parse primary constructors.
public class TicketsController : HubControllerBase
{
    private readonly TicketService tickets;
    private readonly AllocationService allocations;

    public TicketsController(TicketService tickets, AllocationService allocations)
    {
        this.tickets = tickets;
        this.allocations = allocations;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Requesters)]
    public async Task<ActionResult<TicketResponse>> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var result = await tickets.CreateAsync(CurrentCaller, request, ct);
        return result.Error is { } error
            ? Problem(error)
            : CreatedAtAction(nameof(Get), new { ticketId = result.Value!.Id }, result.Value);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<TicketResponse>>> List([FromQuery] TicketQuery query, CancellationToken ct)
    {
        var result = await tickets.ListAsync(CurrentCaller, query, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }

    [HttpGet("{ticketId:guid}")]
    public async Task<ActionResult<TicketResponse>> Get(Guid ticketId, CancellationToken ct)
    {
        var result = await tickets.GetAsync(CurrentCaller, ticketId, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }

    // Changes contents while pending, or moves the state: approve/reject (IT director), close (administrator).
    [HttpPut("{ticketId:guid}")]
    public async Task<ActionResult<TicketResponse>> Update(Guid ticketId, UpdateTicketRequest request, CancellationToken ct)
    {
        var result = await tickets.UpdateAsync(CurrentCaller, ticketId, request, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }

    [HttpDelete("{ticketId:guid}")]
    public async Task<IActionResult> Delete(Guid ticketId, CancellationToken ct)
    {
        var result = await tickets.DeleteAsync(CurrentCaller, ticketId, ct);
        return result.Error is { } error ? Problem(error) : NoContent();
    }

    [HttpPost("{ticketId:guid}/allocate")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<ActionResult<AllocationResultResponse>> Allocate(Guid ticketId, CancellationToken ct)
    {
        var result = await allocations.AllocateAsync(CurrentCaller, ticketId, ct);
        return result.Error is { } error ? Problem(error) : result.Value!;
    }
}
