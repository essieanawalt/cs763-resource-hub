// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the allocation engine, device return and active allocations listing (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: Medium

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Security;

namespace ResourceHub.Api.Services;

public sealed class AllocationService
{
    private readonly HubDbContext db;
    private readonly TimeProvider clock;
    private readonly ILogger<AllocationService> logger;

    public AllocationService(HubDbContext db, TimeProvider clock, ILogger<AllocationService> logger)
    {
        this.db = db;
        this.clock = clock;
        this.logger = logger;
    }

    // Requirement 4 and spec 6.9/6.10: all or nothing, in one transaction. The ticket row and
    // the asset rows are locked (assets in id order, so two allocations cannot deadlock) before
    // stock is read, so concurrent allocations cannot both spend the same stock.
    public async Task<Result<AllocationResultResponse>> AllocateAsync(Caller caller, Guid ticketId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var lockedTicket = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM tickets WHERE \"Id\" = {ticketId} FOR UPDATE")
            .ToListAsync(ct);
        if (lockedTicket.Count == 0)
        {
            return ServiceError.NotFound("Ticket");
        }

        var ticket = await db.Tickets.Include(t => t.Items).FirstAsync(t => t.Id == ticketId, ct);
        var now = clock.GetUtcNow();

        if (ticket.Status != TicketStatus.Approved)
        {
            var reason = $"Ticket is {ticket.Status.ToString().ToLowerInvariant()}; only approved tickets can be allocated.";
            db.TicketEvents.Add(TicketService.NewEvent(ticket.Id, TicketEventType.AllocationFailed, caller.UserId, now, reason));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            logger.LogWarning("Allocation of ticket {TicketId} refused: status {Status}", ticket.Id, ticket.Status);
            return ServiceError.Conflict(reason);
        }

        var assetIds = ticket.Items.Select(i => i.AssetId).Distinct().ToArray();
        await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM assets WHERE \"Id\" = ANY({assetIds}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(ct);
        var assets = await db.Assets.Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

        // Check every item before changing anything.
        var shortages = new List<StockShortage>();
        var keysByAsset = new Dictionary<Guid, List<LicenseKey>>();
        foreach (var item in ticket.Items)
        {
            var asset = assets[item.AssetId];
            switch (asset.Type)
            {
                case AssetType.Physical:
                    if (asset.AvailableUnits!.Value < item.Quantity!.Value)
                    {
                        shortages.Add(new StockShortage(asset.Id, asset.Name, item.Quantity.Value, asset.AvailableUnits.Value));
                    }
                    break;
                case AssetType.License:
                    var keys = await db.LicenseKeys
                        .Where(k => k.AssetId == asset.Id && k.IssuedAt == null)
                        .OrderBy(k => k.Id)
                        .Take(item.Quantity!.Value)
                        .ToListAsync(ct);
                    if (keys.Count < item.Quantity.Value)
                    {
                        shortages.Add(new StockShortage(asset.Id, asset.Name, item.Quantity.Value, keys.Count));
                    }
                    keysByAsset[asset.Id] = keys;
                    break;
                case AssetType.Voucher:
                    // Requirement 2: the balance may never drop below zero.
                    if (asset.Balance!.Value < item.Amount!.Value)
                    {
                        shortages.Add(new StockShortage(asset.Id, asset.Name, item.Amount.Value, asset.Balance.Value));
                    }
                    break;
            }
        }

        if (shortages.Count > 0)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            // The failed attempt is still logged (spec 6.13); the ticket stays approved (spec 6.9).
            var detail = "Not enough stock: " + string.Join("; ",
                shortages.Select(s => $"{s.AssetName} requested {s.Requested:0.##}, available {s.Available:0.##}"));
            db.TicketEvents.Add(TicketService.NewEvent(ticketId, TicketEventType.AllocationFailed, caller.UserId, now, detail));
            await db.SaveChangesAsync(ct);

            logger.LogWarning("Allocation of ticket {TicketId} failed: {ShortageCount} asset(s) short",
                ticketId, shortages.Count);
            return new ServiceError(StatusCodes.Status409Conflict, "Not enough stock",
                "Nothing was allocated; the ticket stays approved and allocation can be run again later.")
            {
                Extensions = { ["shortages"] = shortages },
            };
        }

        var devices = new List<DeviceAllocationResponse>();
        var issuedKeys = new List<LicenseKeyAllocationResponse>();
        var vouchers = new List<VoucherAllocationResponse>();

        foreach (var item in ticket.Items)
        {
            var asset = assets[item.AssetId];
            asset.UpdatedAt = now;
            switch (asset.Type)
            {
                case AssetType.Physical:
                    asset.AvailableUnits -= item.Quantity!.Value;
                    var due = now.AddDays(item.LoanDays ?? asset.DefaultLoanDays!.Value);
                    for (var n = 0; n < item.Quantity.Value; n++)
                    {
                        var assignment = new HardwareAssignment
                        {
                            Id = Guid.NewGuid(),
                            Token = NewHardwareToken(),
                            AssetId = asset.Id,
                            TicketId = ticket.Id,
                            CheckedOutAt = now,
                            DueAt = due,
                        };
                        db.HardwareAssignments.Add(assignment);
                        devices.Add(ToDevice(assignment, asset, now));
                    }
                    break;
                case AssetType.License:
                    foreach (var key in keysByAsset[asset.Id])
                    {
                        key.TicketId = ticket.Id;
                        key.IssuedAt = now;
                        issuedKeys.Add(new LicenseKeyAllocationResponse(asset.Id, asset.Name, key.Value, ticket.Id, now));
                    }
                    break;
                case AssetType.Voucher:
                    asset.Balance -= item.Amount!.Value;
                    db.VoucherAllocations.Add(new VoucherAllocation
                    {
                        Id = Guid.NewGuid(),
                        AssetId = asset.Id,
                        TicketId = ticket.Id,
                        Amount = item.Amount.Value,
                        AllocatedAt = now,
                    });
                    vouchers.Add(new VoucherAllocationResponse(asset.Id, asset.Name, item.Amount.Value, ticket.Id, now));
                    break;
            }
        }

        ticket.Status = TicketStatus.Allocated;
        ticket.AllocatedAt = now;
        ticket.AllocatedById = caller.UserId;
        ticket.UpdatedAt = now;
        // Counts only: key values and tokens stay out of the event log.
        db.TicketEvents.Add(TicketService.NewEvent(ticket.Id, TicketEventType.AllocationSucceeded, caller.UserId, now,
            $"{devices.Count} device(s), {issuedKeys.Count} license key(s), {vouchers.Sum(v => v.Amount):0.00} credit"));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Ticket {TicketId} allocated by {UserId}: {Devices} device(s), {Keys} key(s), {Credit} credit",
            ticket.Id, caller.UserId, devices.Count, issuedKeys.Count, vouchers.Sum(v => v.Amount));
        return new AllocationResultResponse(ticket.Id, ticket.Status, now, devices, issuedKeys, vouchers);
    }

    // Spec 6.11: one device per call, by its hardware assignment token.
    public async Task<Result<ReturnDeviceResponse>> ReturnAsync(Caller caller, Guid assetId, string token, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM assets WHERE \"Id\" = {assetId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
        {
            return ServiceError.NotFound("Asset");
        }

        var asset = await db.Assets.FirstAsync(a => a.Id == assetId, ct);
        if (asset.Type != AssetType.Physical)
        {
            return ServiceError.Conflict("Only physical devices are returned.");
        }

        var trimmed = token.Trim();
        var assignment = await db.HardwareAssignments
            .FirstOrDefaultAsync(h => h.Token == trimmed && h.AssetId == assetId, ct);
        if (assignment is null)
        {
            return ServiceError.NotFound("A device with that token for this asset");
        }
        if (assignment.ReturnedAt is not null)
        {
            return ServiceError.Conflict("That device has already been returned.");
        }

        var now = clock.GetUtcNow();
        var wasOverdue = assignment.IsOverdue(now);
        assignment.ReturnedAt = now;
        assignment.ReturnedById = caller.UserId;
        asset.AvailableUnits += 1;
        asset.UpdatedAt = now;
        db.TicketEvents.Add(TicketService.NewEvent(assignment.TicketId, TicketEventType.DeviceReturned, caller.UserId, now,
            $"{asset.Name} returned{(wasOverdue ? " (overdue)" : string.Empty)}"));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Device of asset {AssetId} returned for ticket {TicketId}; overdue {Overdue}",
            assetId, assignment.TicketId, wasOverdue);
        return new ReturnDeviceResponse(assignment.Token, assetId, assignment.TicketId, assignment.CheckedOutAt,
            assignment.DueAt, now, wasOverdue, asset.AvailableUnits!.Value);
    }

    // Spec 6.12: devices not yet returned, plus keys and vouchers whose ticket is not closed.
    public async Task<Result<ActiveAllocationsResponse>> ActiveAsync(Caller caller, AllocationQuery q, CancellationToken ct)
    {
        if ((q.CourseId is null) == (q.ResearchLabId is null))
        {
            return ServiceError.BadRequest("Give exactly one of courseId or researchLabId.");
        }

        var exists = q.CourseId is { } c
            ? await db.Courses.AnyAsync(x => x.Id == c, ct)
            : await db.ResearchLabs.AnyAsync(x => x.Id == q.ResearchLabId, ct);
        if (!exists)
        {
            return ServiceError.NotFound(q.CourseId is not null ? "Course" : "Research lab");
        }
        if (!caller.CanSeeGroup(q.CourseId, q.ResearchLabId))
        {
            return ServiceError.Forbidden("You can only see allocations for your own course or research lab.");
        }

        var groupTickets = db.Tickets.Where(t =>
            (q.CourseId != null && t.CourseId == q.CourseId) || (q.ResearchLabId != null && t.ResearchLabId == q.ResearchLabId));
        var openTicketIds = groupTickets.Where(t => t.Status != TicketStatus.Closed).Select(t => t.Id);
        var allTicketIds = groupTickets.Select(t => t.Id);

        var now = clock.GetUtcNow();
        var assignments = await db.HardwareAssignments.AsNoTracking()
            .Include(h => h.Asset)
            .Where(h => h.ReturnedAt == null && allTicketIds.Contains(h.TicketId))
            .OrderBy(h => h.DueAt).ThenBy(h => h.Token)
            .ToListAsync(ct);
        var devices = assignments.Select(h => ToDevice(h, h.Asset!, now)).ToList();

        var keys = await (
                from k in db.LicenseKeys
                join a in db.Assets on k.AssetId equals a.Id
                where k.TicketId != null && openTicketIds.Contains(k.TicketId.Value)
                orderby k.IssuedAt descending, k.Id
                select new LicenseKeyAllocationResponse(a.Id, a.Name, k.Value, k.TicketId!.Value, k.IssuedAt!.Value))
            .ToListAsync(ct);

        var vouchers = await db.VoucherAllocations.AsNoTracking()
            .Where(v => openTicketIds.Contains(v.TicketId))
            .OrderByDescending(v => v.AllocatedAt).ThenBy(v => v.Id)
            .Select(v => new VoucherAllocationResponse(v.AssetId, v.Asset!.Name, v.Amount, v.TicketId, v.AllocatedAt))
            .ToListAsync(ct);

        var metrics = new AllocationMetrics(
            devices.Count(d => d.Status == AvailabilityFlag.Checkedout),
            devices.Count(d => d.Status == AvailabilityFlag.Overdue),
            keys.Count,
            vouchers.Sum(v => v.Amount));

        return new ActiveAllocationsResponse(q.CourseId, q.ResearchLabId, metrics, devices, keys, vouchers);
    }

    private static DeviceAllocationResponse ToDevice(HardwareAssignment h, Asset asset, DateTimeOffset now) =>
        new(h.Token, asset.Id, asset.Name, asset.Category, h.TicketId, h.CheckedOutAt, h.DueAt,
            h.IsOverdue(now) ? AvailabilityFlag.Overdue : AvailabilityFlag.Checkedout,
            Math.Round((now - h.CheckedOutAt).TotalDays, 2));

    // 96 random bits; unguessable, so a token alone identifies one device loan.
    private static string NewHardwareToken() => "HW-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
}
