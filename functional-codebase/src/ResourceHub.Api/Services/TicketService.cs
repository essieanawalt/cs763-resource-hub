// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the help desk ticket logic: submit, list, read, update, sign-off, close, delete (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: Medium

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Security;

namespace ResourceHub.Api.Services;

public sealed class TicketService
{
    private readonly HubDbContext db;
    private readonly TimeProvider clock;
    private readonly ILogger<TicketService> logger;

    public TicketService(HubDbContext db, TimeProvider clock, ILogger<TicketService> logger)
    {
        this.db = db;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<Result<TicketResponse>> CreateAsync(Caller caller, CreateTicketRequest req, CancellationToken ct)
    {
        if ((req.CourseId is null) == (req.ResearchLabId is null))
        {
            return ServiceError.BadRequest("Give exactly one of courseId or researchLabId.");
        }

        // Requesters can only ask on behalf of their own course or lab.
        var ownGroup = caller.Role switch
        {
            Roles.CourseInstructor => req.CourseId is not null && req.CourseId == caller.CourseId,
            Roles.LabAdvisor => req.ResearchLabId is not null && req.ResearchLabId == caller.ResearchLabId,
            _ => false,
        };
        if (!ownGroup)
        {
            return ServiceError.Forbidden("You can only submit tickets for your own course or research lab.");
        }

        var projectError = await CheckProjectAsync(req.ProjectId, req.CourseId, req.ResearchLabId, ct);
        if (projectError is not null)
        {
            return projectError;
        }

        var items = await BuildItemsAsync(req.Items, ct);
        if (items.Error is not null)
        {
            return items.Error;
        }

        var now = clock.GetUtcNow();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            RequesterId = caller.UserId,
            CourseId = req.CourseId,
            ResearchLabId = req.ResearchLabId,
            ProjectId = req.ProjectId,
            Notes = NullIfBlank(req.Notes),
            Status = TicketStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var item in items.Value!)
        {
            item.TicketId = ticket.Id;
            ticket.Items.Add(item);
        }
        ticket.Events.Add(NewEvent(ticket.Id, TicketEventType.Submitted, caller.UserId, now,
            $"{ticket.Items.Count} item(s) requested"));

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Ticket {TicketId} submitted by {UserId} with {ItemCount} item(s)",
            ticket.Id, caller.UserId, ticket.Items.Count);
        return (await GetAsync(caller, ticket.Id, ct)).Value!;
    }

    public async Task<Result<PagedResponse<TicketResponse>>> ListAsync(Caller caller, TicketQuery q, CancellationToken ct)
    {
        IQueryable<Ticket> query = VisibleTo(caller, db.Tickets.AsNoTracking());

        if (q.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }
        if (q.CourseId is { } courseId)
        {
            query = query.Where(t => t.CourseId == courseId);
        }
        if (q.ResearchLabId is { } labId)
        {
            query = query.Where(t => t.ResearchLabId == labId);
        }
        if (q.ProjectId is { } projectId)
        {
            query = query.Where(t => t.ProjectId == projectId);
        }
        if (!string.IsNullOrWhiteSpace(q.AssetCategory))
        {
            var category = q.AssetCategory.Trim().ToLowerInvariant();
            query = query.Where(t => t.Items.Any(i => i.Asset!.Category.ToLower() == category));
        }

        var total = await query.CountAsync(ct);
        var tickets = await query
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Include(t => t.Items).ThenInclude(i => i.Asset)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResponse<TicketResponse>(tickets.Select(t => ToResponse(t)).ToList(), q.Page, q.PageSize, total);
    }

    public async Task<Result<TicketResponse>> GetAsync(Caller caller, Guid id, CancellationToken ct)
    {
        var ticket = await VisibleTo(caller, db.Tickets.AsNoTracking())
            .Include(t => t.Items).ThenInclude(i => i.Asset)
            .Include(t => t.Events)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return ServiceError.NotFound("Ticket");
        }

        var events = ticket.Events.OrderBy(e => e.At).ThenBy(e => e.Id).ToList();
        return ToResponse(ticket, events, Timings(ticket, events));
    }

    public async Task<Result<TicketResponse>> UpdateAsync(Caller caller, Guid id, UpdateTicketRequest req, CancellationToken ct)
    {
        var ticket = await VisibleTo(caller, db.Tickets)
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return ServiceError.NotFound("Ticket");
        }

        var error = req.Status is { } target
            ? ChangeState(caller, ticket, target, req)
            : await ChangeContentsAsync(caller, ticket, req, ct);
        if (error is not null)
        {
            return error;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceError.Conflict("The ticket was changed by another request. Read it again and retry.");
        }

        logger.LogInformation("Ticket {TicketId} updated by {UserId}; status {Status}", ticket.Id, caller.UserId, ticket.Status);
        db.ChangeTracker.Clear();
        return await GetAsync(caller, id, ct);
    }

    public async Task<Result<Done>> DeleteAsync(Caller caller, Guid id, CancellationToken ct)
    {
        var ticket = await VisibleTo(caller, db.Tickets).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return ServiceError.NotFound("Ticket");
        }
        if (ticket.RequesterId != caller.UserId && !caller.IsAdministrator)
        {
            return ServiceError.Forbidden("Only the requester or an administrator can delete a ticket.");
        }
        if (ticket.Status != TicketStatus.Pending)
        {
            return ServiceError.Conflict($"Only pending tickets can be deleted; this one is {Name(ticket.Status)}.");
        }

        db.Tickets.Remove(ticket);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceError.Conflict("The ticket was changed by another request. Read it again and retry.");
        }

        logger.LogInformation("Ticket {TicketId} deleted by {UserId}", id, caller.UserId);
        return Done.Value;
    }

    // Spec 6.8: pending -> approved/rejected (IT director), allocated -> closed (administrator).
    private ServiceError? ChangeState(Caller caller, Ticket ticket, TicketStatus target, UpdateTicketRequest req)
    {
        if (req.Items is not null || req.ProjectId is not null || req.Notes is not null)
        {
            return ServiceError.BadRequest("Send either a status change or new contents, not both.");
        }
        if (!Enum.IsDefined(target))
        {
            return ServiceError.BadRequest("Unknown status.");
        }
        if (target == TicketStatus.Allocated)
        {
            return ServiceError.Conflict("Tickets are allocated with POST /api/v1/assets/tickets/{ticket_id}/allocate.");
        }

        var now = clock.GetUtcNow();
        var comment = NullIfBlank(req.Comment);
        switch (ticket.Status, target)
        {
            case (TicketStatus.Pending, TicketStatus.Approved or TicketStatus.Rejected):
                if (!caller.IsItDirector)
                {
                    return ServiceError.Forbidden("Only the IT director can approve or reject tickets.");
                }
                ticket.Status = target;
                ticket.SignedOffAt = now;
                ticket.SignedOffById = caller.UserId;
                db.TicketEvents.Add(NewEvent(ticket.Id,
                    target == TicketStatus.Approved ? TicketEventType.Approved : TicketEventType.Rejected,
                    caller.UserId, now, comment));
                break;

            case (TicketStatus.Allocated, TicketStatus.Closed):
                if (!caller.IsAdministrator)
                {
                    return ServiceError.Forbidden("Only an administrator can close tickets.");
                }
                ticket.Status = TicketStatus.Closed;
                ticket.ClosedAt = now;
                db.TicketEvents.Add(NewEvent(ticket.Id, TicketEventType.Closed, caller.UserId, now, comment));
                break;

            default:
                return ServiceError.Conflict($"A ticket cannot move from {Name(ticket.Status)} to {Name(target)}.");
        }

        ticket.UpdatedAt = now;
        return null;
    }

    private async Task<ServiceError?> ChangeContentsAsync(Caller caller, Ticket ticket, UpdateTicketRequest req, CancellationToken ct)
    {
        if (req.Comment is not null)
        {
            return ServiceError.BadRequest("A comment goes with a status change.");
        }
        if (ticket.RequesterId != caller.UserId)
        {
            return ServiceError.Forbidden("Only the requester can change a ticket's contents.");
        }
        if (ticket.Status != TicketStatus.Pending)
        {
            return ServiceError.Conflict($"Only pending tickets can be changed; this one is {Name(ticket.Status)}.");
        }
        if (req.Items is null || req.Items.Count == 0)
        {
            return ServiceError.BadRequest("Give the ticket's items (at least one), or a status.");
        }

        var projectError = await CheckProjectAsync(req.ProjectId, ticket.CourseId, ticket.ResearchLabId, ct);
        if (projectError is not null)
        {
            return projectError;
        }

        var items = await BuildItemsAsync(req.Items, ct);
        if (items.Error is not null)
        {
            return items.Error;
        }

        // Update matching rows in place so the (ticket, asset) unique index is never hit mid-save.
        var wanted = items.Value!.ToDictionary(i => i.AssetId);
        foreach (var existing in ticket.Items.ToList())
        {
            if (wanted.Remove(existing.AssetId, out var replacement))
            {
                existing.Quantity = replacement.Quantity;
                existing.Amount = replacement.Amount;
                existing.LoanDays = replacement.LoanDays;
            }
            else
            {
                db.TicketItems.Remove(existing);
            }
        }
        foreach (var added in wanted.Values)
        {
            added.TicketId = ticket.Id;
            db.TicketItems.Add(added);
        }

        var now = clock.GetUtcNow();
        ticket.ProjectId = req.ProjectId;
        ticket.Notes = NullIfBlank(req.Notes);
        ticket.UpdatedAt = now;
        db.TicketEvents.Add(NewEvent(ticket.Id, TicketEventType.Updated, caller.UserId, now,
            $"{req.Items.Count} item(s) requested"));
        return null;
    }

    private async Task<ServiceError?> CheckProjectAsync(Guid? projectId, Guid? courseId, Guid? labId, CancellationToken ct)
    {
        if (projectId is null)
        {
            return null;
        }
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
        {
            return ServiceError.BadRequest("Unknown project.");
        }
        var matches = (courseId is not null && project.CourseId == courseId)
            || (labId is not null && project.ResearchLabId == labId);
        return matches ? null : ServiceError.BadRequest("The project does not belong to the ticket's course or research lab.");
    }

    // Spec 6.7: quantity for physical and license assets, amount for vouchers.
    private async Task<Result<List<TicketItem>>> BuildItemsAsync(List<TicketItemRequest> requested, CancellationToken ct)
    {
        var ids = requested.Select(i => i.AssetId!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count)
        {
            return ServiceError.BadRequest("Each asset can appear only once in a ticket.");
        }

        var assets = await db.Assets.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var items = new List<TicketItem>();
        foreach (var r in requested)
        {
            if (!assets.TryGetValue(r.AssetId!.Value, out var asset))
            {
                return ServiceError.BadRequest($"Unknown asset {r.AssetId}.");
            }

            if (asset.Type == AssetType.Voucher)
            {
                if (r.Amount is null || r.Quantity is not null || r.LoanDays is not null)
                {
                    return ServiceError.BadRequest($"Voucher asset {asset.Id} needs an amount and nothing else.");
                }
                if (decimal.Round(r.Amount.Value, 2) != r.Amount.Value)
                {
                    return ServiceError.BadRequest("Amounts can have at most two decimal places.");
                }
            }
            else
            {
                if (r.Quantity is null || r.Amount is not null)
                {
                    return ServiceError.BadRequest($"Asset {asset.Id} needs a quantity, not an amount.");
                }
                if (r.LoanDays is not null && asset.Type != AssetType.Physical)
                {
                    return ServiceError.BadRequest("A loan period applies to physical assets only.");
                }
            }

            items.Add(new TicketItem
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                Quantity = r.Quantity,
                Amount = r.Amount,
                LoanDays = r.LoanDays,
            });
        }
        return items;
    }

    // Requesters see their own course's or lab's tickets; IT director and administrator see all.
    // Hidden tickets read as 404 so their ids cannot be probed.
    public static IQueryable<Ticket> VisibleTo(Caller caller, IQueryable<Ticket> tickets)
    {
        if (caller.IsStaff)
        {
            return tickets;
        }
        var courseId = caller.CourseId;
        var labId = caller.ResearchLabId;
        return tickets.Where(t =>
            (courseId != null && t.CourseId == courseId) || (labId != null && t.ResearchLabId == labId));
    }

    public static TicketEvent NewEvent(Guid ticketId, TicketEventType type, Guid actorId, DateTimeOffset at, string? detail) =>
        new() { Id = Guid.NewGuid(), TicketId = ticketId, Type = type, ActorId = actorId, At = at, Detail = detail };

    private static TicketTimings Timings(Ticket t, List<TicketEvent> events)
    {
        static double? Between(DateTimeOffset? from, DateTimeOffset? to) =>
            from is not null && to is not null ? Math.Round((to.Value - from.Value).TotalSeconds, 3) : null;

        var approvedAt = t.Status is TicketStatus.Rejected ? null : t.SignedOffAt;
        var attempts = events.Count(e => e.Type is TicketEventType.AllocationSucceeded or TicketEventType.AllocationFailed);
        var failed = events.Count(e => e.Type == TicketEventType.AllocationFailed);
        return new TicketTimings(
            Between(t.CreatedAt, t.SignedOffAt),
            Between(approvedAt, t.AllocatedAt),
            Between(t.AllocatedAt, t.ClosedAt),
            attempts,
            failed);
    }

    private static TicketResponse ToResponse(Ticket t, List<TicketEvent>? events = null, TicketTimings? timings = null) =>
        new(t.Id, t.Status, t.RequesterId, t.CourseId, t.ResearchLabId, t.ProjectId, t.Notes,
            t.Items.OrderBy(i => i.Asset?.Name).Select(i => new TicketItemResponse(
                i.AssetId, i.Asset?.Name ?? string.Empty, i.Asset?.Type ?? default, i.Asset?.Category ?? string.Empty,
                i.Quantity, i.Amount, i.LoanDays)).ToList(),
            t.CreatedAt, t.UpdatedAt, t.SignedOffAt, t.SignedOffById, t.AllocatedAt, t.ClosedAt,
            events?.Select(e => new TicketEventResponse(e.Type, e.ActorId, e.At, e.Detail)).ToList(),
            timings);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Name(TicketStatus s) => s.ToString().ToLowerInvariant();
}
