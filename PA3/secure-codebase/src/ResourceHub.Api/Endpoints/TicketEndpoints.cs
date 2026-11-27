// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Ticket endpoints with role checks (403) and row-level checks (404) on every read, update, delete and listing (CWE-862); soft delete keeps history (CWE-221); audited writes (T2); log lines through the generated HubLog methods (CA1873) (~100%)
// Human Modifications: None yet
// Verification: TicketAuthorizationTests, TicketLifecycleTests, TicketSubmitTests, TicketQueryTests, AuditTrailTests (all run as the runtime database role); dotnet build with CA1873 as a warning
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Endpoints;

/// <summary>
/// Authorization rule for tickets: a role that may never do an action gets 403 before any record
/// is looked at. Otherwise the record is checked against the caller, and a ticket the caller may
/// not touch answers 404, exactly like one that does not exist.
/// admin: read, list, approve, reject, close, allocate and delete (pending) any ticket.
/// faculty_instructor: submit for their own course or lab; read and list only tickets they
/// submitted; edit or delete those only while pending.
/// </summary>
public static class TicketEndpoints
{
    private const string Entity = "ticket";

    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/api/v1/assets/tickets");

        tickets.MapPost("/", SubmitAsync).Audited("ticket.submit", Entity);
        tickets.MapGet("/", ListAsync);
        tickets.MapGet("/{ticketId:guid}", GetAsync);
        tickets.MapPut("/{ticketId:guid}", UpdateAsync).Audited("ticket.update", Entity, "ticketId");
        tickets.MapDelete("/{ticketId:guid}", DeleteAsync).Audited("ticket.delete", Entity, "ticketId");
        tickets.MapPost("/{ticketId:guid}/allocate", AllocateAsync).Audited("ticket.allocate", Entity, "ticketId");
    }

    /// <summary>Row-level read rule: admins read every ticket, faculty only the ones they submitted.</summary>
    public static bool CanRead(Caller caller, Ticket ticket) =>
        caller.IsAdmin || (caller.IsFaculty && ticket.RequesterId == caller.Id);

    /// <summary>Row-level edit/delete rule for faculty: only their own tickets.</summary>
    private static bool IsOwnTicket(Caller caller, Ticket ticket) =>
        caller.IsFaculty && ticket.RequesterId == caller.Id;

    private static IResult TicketNotFound() => Problems.NotFound("Ticket not found.");

    private static async Task<IResult> AllocateAsync(
        Guid ticketId, HttpContext http, HubDbContext db, AllocationService allocations, CancellationToken ct)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);
        return Callers.RequireRole(caller, Roles.Admin) ?? await allocations.AllocateAsync(ticketId, caller, ct);
    }

    /// <summary>Locks the ticket row for the rest of the transaction so concurrent changes queue up. Deleted tickets are not found.</summary>
    private static Task<Ticket?> LockTicketAsync(HubDbContext db, Guid ticketId, CancellationToken ct) =>
        db.Tickets.FromSql($"SELECT * FROM tickets WHERE \"Id\" = {ticketId} FOR UPDATE").FirstOrDefaultAsync(ct);

    private static async Task<IResult> UpdateAsync(
        Guid ticketId, TicketRequest body, HttpContext http, HubDbContext db, AuditTrail audit, TimeProvider clock,
        ILogger<TicketRequest> log, CancellationToken ct)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);

        if (body.Status is not null)
        {
            var hasContent = body.CourseId is not null || body.ResearchLabId is not null || body.ProjectId is not null
                             || body.Notes is not null || body.Items is not null;
            if (hasContent)
            {
                return Problems.BadRequest("status", "Send either a status change or new content, not both.");
            }
            // Approve, reject and close are admin-only: 403 for any other role, whatever the ticket.
            return Callers.RequireRole(caller, Roles.Admin)
                   ?? await TransitionAsync(ticketId, body.Status.Value, body.Reason, caller, db, audit, clock, log, ct);
        }

        // Content edits: only the faculty requester, only while pending.
        if (Callers.RequireRole(caller, Roles.Faculty) is { } denied)
        {
            return denied;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockTicketAsync(db, ticketId, ct);
        if (ticket is null || !IsOwnTicket(caller, ticket))
        {
            return TicketNotFound();
        }
        if (ticket.Status != TicketStatus.Pending)
        {
            return Problems.Conflict("Only pending tickets can be changed.", $"The ticket is {Lower(ticket.Status)}.");
        }

        var (content, invalid) = await TicketContent.ValidateAsync(db, caller, body, ct);
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
        var summary = $"{content.Items.Count} asset(s) requested";
        db.TicketEvents.Add(TicketEvents.Create(ticketId, TicketEventType.Updated, TicketStatus.Pending, TicketStatus.Pending, caller.Id, now, summary));
        audit.Record(caller, "ticket.update", Entity, ticketId, detail: summary);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.TicketContentUpdated(ticketId, caller.Id);

        var saved = await WithItems(db).AsNoTracking().FirstAsync(t => t.Id == ticketId, ct);
        return Results.Ok(TicketViews.ToDto(saved));
    }

    /// <summary>
    /// pending -> approved | rejected and allocated -> closed (admin; checked by the caller).
    /// approved -> allocated only happens through POST .../allocate (spec 6.8).
    /// </summary>
    private static async Task<IResult> TransitionAsync(
        Guid ticketId, TicketStatus target, string? reason, Caller caller, HubDbContext db, AuditTrail audit, TimeProvider clock,
        ILogger log, CancellationToken ct)
    {
        var errors = new Errors();
        reason = Validation.Text(errors, "reason", reason, 500, required: false);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockTicketAsync(db, ticketId, ct);
        if (ticket is null || !CanRead(caller, ticket))
        {
            return TicketNotFound();
        }

        TicketStatus requiredFrom;
        TicketEventType eventType;
        switch (target)
        {
            case TicketStatus.Approved:
                (requiredFrom, eventType) = (TicketStatus.Pending, TicketEventType.Approved);
                break;
            case TicketStatus.Rejected:
                (requiredFrom, eventType) = (TicketStatus.Pending, TicketEventType.Rejected);
                break;
            case TicketStatus.Closed:
                (requiredFrom, eventType) = (TicketStatus.Allocated, TicketEventType.Closed);
                break;
            case TicketStatus.Allocated:
                return Problems.Conflict("Use the allocate command.", "Tickets become allocated only through POST /api/v1/assets/tickets/{ticket_id}/allocate.");
            default:
                return Problems.Conflict("Tickets cannot move back to pending.");
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
        db.TicketEvents.Add(TicketEvents.Create(ticketId, eventType, from, target, caller.Id, now, reason));
        audit.Record(caller, target switch
        {
            TicketStatus.Approved => "ticket.approve",
            TicketStatus.Rejected => "ticket.reject",
            _ => "ticket.close",
        }, Entity, ticketId, detail: $"{Lower(from)} -> {Lower(target)}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.TicketMoved(ticketId, from, target, caller.Id);

        var saved = await WithItems(db).AsNoTracking().FirstAsync(t => t.Id == ticketId, ct);
        return Results.Ok(TicketViews.ToDto(saved));
    }

    /// <summary>
    /// Withdraws a pending ticket. The row is kept with DeletedAt set and a "deleted" event, so the
    /// ticket's event history is never removed (CWE-221); the API then treats it as not found.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        Guid ticketId, HttpContext http, HubDbContext db, AuditTrail audit, TimeProvider clock, ILogger<TicketRequest> log, CancellationToken ct)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);
        if (Callers.RequireRole(caller, Roles.Admin, Roles.Faculty) is { } denied)
        {
            return denied;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockTicketAsync(db, ticketId, ct);
        if (ticket is null || !(caller.IsAdmin || IsOwnTicket(caller, ticket)))
        {
            return TicketNotFound();
        }
        if (ticket.Status != TicketStatus.Pending)
        {
            return Problems.Conflict("Only pending tickets can be deleted.", $"The ticket is {Lower(ticket.Status)}.");
        }

        var now = clock.GetUtcNow();
        ticket.DeletedAt = now;
        ticket.UpdatedAt = now;
        db.TicketEvents.Add(TicketEvents.Create(ticketId, TicketEventType.Deleted, TicketStatus.Pending, TicketStatus.Pending, caller.Id, now,
            "Pending ticket withdrawn"));
        audit.Record(caller, "ticket.delete", Entity, ticketId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.PendingTicketDeleted(ticketId, caller.Id);
        return Results.NoContent();
    }

    private static string Lower(TicketStatus status) => status.ToString().ToLowerInvariant();

    private static async Task<IResult> ListAsync(
        HttpContext http, HubDbContext db, CancellationToken ct,
        string? status = null, string? assetCategory = null, Guid? courseId = null, Guid? researchLabId = null,
        Guid? projectId = null, int? page = null, int? pageSize = null)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);
        if (Callers.RequireRole(caller, Roles.Admin, Roles.Faculty) is { } denied)
        {
            return denied;
        }

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

        // Row-level filter first: the listing (and its total) only ever covers rows the caller may read.
        var q = db.Tickets.AsNoTracking();
        if (!caller.IsAdmin)
        {
            var me = caller.Id;
            q = q.Where(t => t.RequesterId == me);
        }

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
        TicketRequest body, HttpContext http, HubDbContext db, AuditTrail audit, TimeProvider clock, ILogger<TicketRequest> log, CancellationToken ct)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);
        if (Callers.RequireRole(caller, Roles.Faculty) is { } denied)
        {
            return denied;
        }

        var (content, invalid) = await TicketContent.ValidateAsync(db, caller, body, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            RequesterId = caller.Id,   // always the token's sub, never a body field
            CourseId = content!.CourseId,
            ResearchLabId = content.ResearchLabId,
            ProjectId = content.ProjectId,
            Notes = content.Notes,
            Status = TicketStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Items = content.Items,
        };
        var summary = $"{content.Items.Count} asset(s) requested";
        ticket.Events.Add(TicketEvents.Create(ticket.Id, TicketEventType.Submitted, null, TicketStatus.Pending, caller.Id, now, summary));

        db.Tickets.Add(ticket);
        audit.Record(caller, "ticket.submit", Entity, ticket.Id, detail: summary);
        await db.SaveChangesAsync(ct);
        log.TicketSubmitted(ticket.Id, caller.Id, ticket.Items.Count);

        var saved = await WithItems(db).AsNoTracking().FirstAsync(t => t.Id == ticket.Id, ct);
        return Results.Created($"/api/v1/assets/tickets/{ticket.Id}", TicketViews.ToDto(saved));
    }

    private static async Task<IResult> GetAsync(Guid ticketId, HttpContext http, HubDbContext db, CancellationToken ct)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);
        if (Callers.RequireRole(caller, Roles.Admin, Roles.Faculty) is { } denied)
        {
            return denied;
        }

        var ticket = await WithItems(db).Include(t => t.Events).AsNoTracking().AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        return ticket is null || !CanRead(caller, ticket)
            ? TicketNotFound()
            : Results.Ok(TicketViews.ToDto(ticket, detail: true));
    }
}
