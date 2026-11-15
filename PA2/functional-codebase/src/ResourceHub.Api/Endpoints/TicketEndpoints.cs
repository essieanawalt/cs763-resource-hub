// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: /api/v1/assets/tickets routes: submit, list, read, update/transition, delete (~100%)
// Human Modifications: None (review only)
// Verification: TicketSubmitTests, TicketQueryTests, TicketLifecycleTests, TicketMetricsTests; reviewed by me 2026-11-15
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Endpoints;

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/api/v1/assets/tickets");

        tickets.MapPost("/", SubmitAsync);
        tickets.MapGet("/", ListAsync);
        tickets.MapGet("/{ticketId:guid}", GetAsync);
        tickets.MapPut("/{ticketId:guid}", UpdateAsync);
        tickets.MapDelete("/{ticketId:guid}", DeleteAsync);
        tickets.MapPost("/{ticketId:guid}/allocate", AllocateAsync);
    }

    private static async Task<IResult> AllocateAsync(
        Guid ticketId, HttpContext http, HubDbContext db, AllocationService allocations, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct, UserRole.Administrator);
        return denied ?? await allocations.AllocateAsync(ticketId, actor!, ct);
    }

    /// <summary>Locks the ticket row for the rest of the transaction so concurrent changes queue up.</summary>
    private static Task<Ticket?> LockTicketAsync(HubDbContext db, Guid ticketId, CancellationToken ct) =>
        db.Tickets.FromSql($"SELECT * FROM tickets WHERE \"Id\" = {ticketId} FOR UPDATE").FirstOrDefaultAsync(ct);

    private static async Task<IResult> UpdateAsync(
        Guid ticketId, TicketRequest body, HttpContext http, HubDbContext db, TimeProvider clock, ILogger<TicketRequest> log, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (body.Status is not null)
        {
            var hasContent = body.CourseId is not null || body.ResearchLabId is not null || body.ProjectId is not null
                             || body.Notes is not null || body.Items is not null;
            if (hasContent)
            {
                return Problems.BadRequest("status", "Send either a status change or new content, not both.");
            }
            return await TransitionAsync(ticketId, body.Status.Value, body.Reason, actor!, db, clock, log, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockTicketAsync(db, ticketId, ct);
        if (ticket is null)
        {
            return Problems.NotFound("Ticket not found.");
        }
        if (ticket.RequesterId != actor!.Id)
        {
            return Problems.Forbidden("Only the requester can change a ticket's contents.");
        }
        if (ticket.Status != TicketStatus.Pending)
        {
            return Problems.Conflict("Only pending tickets can be changed.", $"The ticket is {ticket.Status.ToString().ToLowerInvariant()}.");
        }

        var (content, invalid) = await TicketContent.ValidateAsync(db, actor, body, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        // Replace the items: delete first, then insert, so the (ticket, asset) unique index never clashes.
        await db.TicketItems.Where(i => i.TicketId == ticketId).ExecuteDeleteAsync(ct);
        foreach (var item in content!.Items)
        {
            item.TicketId = ticketId;
            db.TicketItems.Add(item);
        }

        var now = clock.GetUtcNow();
        ticket.CourseId = content.CourseId;
        ticket.ResearchLabId = content.ResearchLabId;
        ticket.ProjectId = content.ProjectId;
        ticket.Notes = content.Notes;
        ticket.UpdatedAt = now;
        db.TicketEvents.Add(TicketEvents.Create(ticketId, TicketEventType.Updated, TicketStatus.Pending, TicketStatus.Pending, actor.Id, now,
            $"{content.Items.Count} asset(s) requested"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Ticket {TicketId} content updated by {ActorId}", ticketId, actor.Id);

        var saved = await WithItems(db).AsNoTracking().FirstAsync(t => t.Id == ticketId, ct);
        return Results.Ok(TicketViews.ToDto(saved));
    }

    /// <summary>
    /// pending -> approved | rejected (IT director sign-off) and allocated -> closed (administrator).
    /// approved -> allocated only happens through POST .../allocate (spec 6.8).
    /// </summary>
    private static async Task<IResult> TransitionAsync(
        Guid ticketId, TicketStatus target, string? reason, Actor actor, HubDbContext db, TimeProvider clock, ILogger log, CancellationToken ct)
    {
        var errors = new Errors();
        reason = Validation.Text(errors, "reason", reason, 500, required: false);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockTicketAsync(db, ticketId, ct);
        if (ticket is null)
        {
            return Problems.NotFound("Ticket not found.");
        }

        TicketStatus requiredFrom;
        UserRole requiredRole;
        TicketEventType eventType;
        switch (target)
        {
            case TicketStatus.Approved:
                (requiredFrom, requiredRole, eventType) = (TicketStatus.Pending, UserRole.ItDirector, TicketEventType.Approved);
                break;
            case TicketStatus.Rejected:
                (requiredFrom, requiredRole, eventType) = (TicketStatus.Pending, UserRole.ItDirector, TicketEventType.Rejected);
                break;
            case TicketStatus.Closed:
                (requiredFrom, requiredRole, eventType) = (TicketStatus.Allocated, UserRole.Administrator, TicketEventType.Closed);
                break;
            case TicketStatus.Allocated:
                return Problems.Conflict("Use the allocate command.", "Tickets become allocated only through POST /api/v1/assets/tickets/{ticket_id}/allocate.");
            default:
                return Problems.Conflict("Tickets cannot move back to pending.");
        }

        if (actor.Role != requiredRole)
        {
            return Problems.Forbidden($"Moving a ticket to {Lower(target)} needs the {requiredRole} role.");
        }
        if (ticket.Status != requiredFrom)
        {
            return Problems.Conflict($"Cannot move a {Lower(ticket.Status)} ticket to {Lower(target)}.",
                $"Only {Lower(requiredFrom)} tickets can move to {Lower(target)}.");
        }

        var now = clock.GetUtcNow();
        var from = ticket.Status;
        ticket.Status = target;
        ticket.UpdatedAt = now;
        switch (target)
        {
            case TicketStatus.Approved: ticket.ApprovedAt = now; break;
            case TicketStatus.Rejected: ticket.RejectedAt = now; break;
            case TicketStatus.Closed: ticket.ClosedAt = now; break;
        }
        db.TicketEvents.Add(TicketEvents.Create(ticketId, eventType, from, target, actor.Id, now, reason));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Ticket {TicketId} moved from {From} to {To} by {ActorId}", ticketId, from, target, actor.Id);

        var saved = await WithItems(db).AsNoTracking().FirstAsync(t => t.Id == ticketId, ct);
        return Results.Ok(TicketViews.ToDto(saved));
    }

    private static async Task<IResult> DeleteAsync(
        Guid ticketId, HttpContext http, HubDbContext db, ILogger<TicketRequest> log, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockTicketAsync(db, ticketId, ct);
        if (ticket is null)
        {
            return Problems.NotFound("Ticket not found.");
        }
        if (ticket.RequesterId != actor!.Id && actor.Role != UserRole.Administrator)
        {
            return Problems.Forbidden("Only the requester or an administrator can delete a ticket.");
        }
        if (ticket.Status != TicketStatus.Pending)
        {
            return Problems.Conflict("Only pending tickets can be deleted.", $"The ticket is {Lower(ticket.Status)}.");
        }

        db.Tickets.Remove(ticket);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Pending ticket {TicketId} deleted by {ActorId}", ticketId, actor.Id);
        return Results.NoContent();
    }

    private static string Lower(TicketStatus status) => status.ToString().ToLowerInvariant();

    private static async Task<IResult> ListAsync(
        HubDbContext db, CancellationToken ct,
        string? status = null, string? assetCategory = null, Guid? courseId = null, Guid? researchLabId = null,
        Guid? projectId = null, int? page = null, int? pageSize = null)
    {
        var errors = new Errors();
        var statuses = new List<TicketStatus>();
        foreach (var part in (status ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Query.Enum<TicketStatus>(errors, "status", part) is { } parsed)
            {
                statuses.Add(parsed);
            }
        }
        var (p, size) = Query.Paging(errors, page, pageSize);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        var q = db.Tickets.AsNoTracking();
        if (statuses.Count > 0)
        {
            q = q.Where(t => statuses.Contains(t.Status));
        }
        if (courseId is not null)
        {
            q = q.Where(t => t.CourseId == courseId);
        }
        if (researchLabId is not null)
        {
            q = q.Where(t => t.ResearchLabId == researchLabId);
        }
        if (projectId is not null)
        {
            q = q.Where(t => t.ProjectId == projectId);
        }
        if (!string.IsNullOrWhiteSpace(assetCategory))
        {
            var c = assetCategory.Trim().ToLower();
            q = q.Where(t => t.Items.Any(i => i.Asset!.Category.ToLower() == c));
        }

        var total = await q.CountAsync(ct);
        var ids = await q
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(t => t.Id)
            .ToListAsync(ct);
        var rows = await WithItems(db).AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var ordered = ids.Select(id => TicketViews.ToDto(rows.Single(t => t.Id == id))).ToList();

        return Results.Ok(new PagedResult<TicketDto>(ordered, p, size, total));
    }

    private static IQueryable<Ticket> WithItems(HubDbContext db) =>
        db.Tickets.Include(t => t.Items).ThenInclude(i => i.Asset);

    private static async Task<IResult> SubmitAsync(
        TicketRequest body, HttpContext http, HubDbContext db, TimeProvider clock, ILogger<TicketRequest> log, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct, UserRole.CourseInstructor, UserRole.LabAdvisor);
        if (denied is not null)
        {
            return denied;
        }

        var (content, invalid) = await TicketContent.ValidateAsync(db, actor!, body, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            RequesterId = actor!.Id,
            CourseId = content!.CourseId,
            ResearchLabId = content.ResearchLabId,
            ProjectId = content.ProjectId,
            Notes = content.Notes,
            Status = TicketStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Items = content.Items,
        };
        ticket.Events.Add(TicketEvents.Create(ticket.Id, TicketEventType.Submitted, null, TicketStatus.Pending, actor.Id, now,
            $"{content.Items.Count} asset(s) requested"));

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Ticket {TicketId} submitted by {ActorId} with {ItemCount} item(s)", ticket.Id, actor.Id, ticket.Items.Count);

        var saved = await WithItems(db).AsNoTracking().FirstAsync(t => t.Id == ticket.Id, ct);
        return Results.Created($"/api/v1/assets/tickets/{ticket.Id}", TicketViews.ToDto(saved));
    }

    private static async Task<IResult> GetAsync(Guid ticketId, HubDbContext db, CancellationToken ct)
    {
        var ticket = await WithItems(db).Include(t => t.Events).AsNoTracking().AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        return ticket is null
            ? Problems.NotFound("Ticket not found.")
            : Results.Ok(TicketViews.ToDto(ticket, detail: true));
    }
}
