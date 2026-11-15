// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Replace primary constructors with regular constructors so Semgrep's C# parser can read the file"
// AI Contribution: The allocation engine: atomic, all-or-nothing deductions that issue keys, credits and device tokens (~100%)
// Human Modifications: None yet
// Verification: AllocationTests (incl. concurrent allocations), SeededLicense*Tests; full suite against PostgreSQL 18 (123 passed, 0 failed, 0 skipped); Semgrep 1.180.0 p/csharp: 0 parse errors
// Confidence: High

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

/// <summary>
/// Runs POST /api/v1/assets/tickets/{id}/allocate (requirement 4, spec 6.9 and 6.10).
/// The ticket row and the requested asset rows are locked (in asset id order, so two
/// allocations cannot deadlock) for one transaction. Stock is checked for every item
/// before anything changes; if any item is short, nothing is written except a failure event.
/// </summary>
public sealed class AllocationService
{
    private readonly HubDbContext db;
    private readonly TimeProvider clock;
    private readonly ILogger<AllocationService> log;

    public AllocationService(HubDbContext db, TimeProvider clock, ILogger<AllocationService> log)
    {
        this.db = db;
        this.clock = clock;
        this.log = log;
    }

    public async Task<IResult> AllocateAsync(Guid ticketId, Actor actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var ticket = await db.Tickets
            .FromSql($"SELECT * FROM tickets WHERE \"Id\" = {ticketId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (ticket is null)
        {
            return Problems.NotFound("Ticket not found.");
        }

        if (ticket.Status != TicketStatus.Approved)
        {
            var status = ticket.Status.ToString().ToLowerInvariant();
            await RecordFailureAsync(tx, ticket, actor, now, $"Ticket is {status}, not approved.", ct);
            return Problems.Conflict("Only approved tickets can be allocated.", $"The ticket is {status}.");
        }

        var items = await db.TicketItems.AsNoTracking().Where(i => i.TicketId == ticketId).ToListAsync(ct);
        var assetIds = items.Select(i => i.AssetId).Distinct().OrderBy(id => id).ToArray();
        var assets = await db.Assets
            .FromSql($"SELECT * FROM assets WHERE \"Id\" = ANY({assetIds}) ORDER BY \"Id\" FOR UPDATE")
            .ToDictionaryAsync(a => a.Id, ct);

        var shortages = FindShortages(items, assets);
        if (shortages.Count > 0)
        {
            var detail = string.Join("; ", shortages.Select(s => $"{s.AssetName}: requested {s.Requested}, available {s.Available}"));
            await RecordFailureAsync(tx, ticket, actor, now, "Not enough stock. " + detail, ct);
            log.LogWarning("Allocation of ticket {TicketId} failed for lack of stock on {ShortCount} asset(s)", ticketId, shortages.Count);
            return Problems.Conflict(
                "Not enough stock to allocate this ticket.",
                "Nothing was allocated; the ticket stays approved and allocation can be run again later.",
                new Dictionary<string, object?> { ["shortages"] = shortages });
        }

        var results = new List<AllocationDto>();
        foreach (var item in items.OrderBy(i => assets[i.AssetId].Name).ThenBy(i => i.AssetId))
        {
            var asset = assets[item.AssetId];
            var allocation = new Allocation
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                AssetId = asset.Id,
                AssetType = asset.Type,
                Quantity = item.Quantity,
                Amount = item.Amount,
                AllocatedAt = now,
            };
            db.Allocations.Add(allocation);

            List<string>? keys = null;
            List<DeviceDto>? devices = null;
            switch (asset.Type)
            {
                case AssetType.Physical:
                {
                    var qty = item.Quantity!.Value;
                    asset.RemainingUnits -= qty;
                    var due = now.AddDays(item.LoanDays ?? asset.DefaultLoanDays!.Value);
                    devices = [];
                    for (var n = 0; n < qty; n++)
                    {
                        var token = NewToken();
                        db.DeviceAssignments.Add(new DeviceAssignment
                        {
                            Id = Guid.NewGuid(), Token = token, AllocationId = allocation.Id, AssetId = asset.Id,
                            TicketId = ticketId, CheckedOutAt = now, DueAt = due,
                        });
                        devices.Add(new DeviceDto(token, now, due, null, "checkedout"));
                    }
                    break;
                }
                case AssetType.License:
                {
                    var qty = item.Quantity!.Value;
                    var free = await db.LicenseKeys
                        .Where(k => k.AssetId == asset.Id && k.IssuedTicketId == null)
                        .OrderBy(k => k.Id)
                        .Take(qty)
                        .ToListAsync(ct);
                    foreach (var key in free)
                    {
                        key.IssuedTicketId = ticketId;
                        key.AllocationId = allocation.Id;
                        key.IssuedAt = now;
                    }
                    asset.RemainingUnits -= qty;
                    keys = free.Select(k => k.Key).ToList();
                    break;
                }
                case AssetType.Voucher:
                    asset.Balance -= item.Amount!.Value;
                    break;
            }
            asset.UpdatedAt = now;

            results.Add(new AllocationDto(
                allocation.Id, ticketId, asset.Id, asset.Name, asset.Type, asset.Category, now,
                allocation.Quantity, allocation.Amount, keys, devices));
        }

        ticket.Status = TicketStatus.Allocated;
        ticket.AllocatedAt = now;
        ticket.UpdatedAt = now;
        db.TicketEvents.Add(TicketEvents.Create(ticketId, TicketEventType.AllocationSucceeded,
            TicketStatus.Approved, TicketStatus.Allocated, actor.Id, now, $"{results.Count} asset(s) allocated"));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Ticket {TicketId} allocated by {ActorId}: {AssetCount} asset(s), {DeviceCount} device(s), {KeyCount} key(s), {Credits} credits",
            ticketId, actor.Id, results.Count,
            results.Sum(r => r.Devices?.Count ?? 0), results.Sum(r => r.LicenseKeys?.Count ?? 0), results.Sum(r => r.Amount ?? 0m));

        return Results.Ok(new AllocationResultDto(ticketId, TicketStatus.Allocated, now, results));
    }

    private static List<ShortageDto> FindShortages(List<TicketItem> items, Dictionary<Guid, Asset> assets)
    {
        var shortages = new List<ShortageDto>();
        foreach (var item in items)
        {
            var asset = assets[item.AssetId];
            var (requested, available) = asset.Type == AssetType.Voucher
                ? (item.Amount ?? 0m, asset.Balance ?? 0m)
                : (item.Quantity ?? 0, (decimal)(asset.RemainingUnits ?? 0));
            if (requested > available)
            {
                shortages.Add(new ShortageDto(asset.Id, asset.Name, requested, available));
            }
        }
        return shortages;
    }

    /// <summary>Rolls back anything pending, then logs the failed attempt (spec 6.13).</summary>
    private async Task RecordFailureAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Ticket ticket, Actor actor, DateTimeOffset now, string detail, CancellationToken ct)
    {
        await tx.RollbackAsync(ct);
        db.ChangeTracker.Clear();
        db.TicketEvents.Add(TicketEvents.Create(ticket.Id, TicketEventType.AllocationFailed, ticket.Status, ticket.Status, actor.Id, now, detail));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Hardware assignment token: unguessable, so it can identify a device at return.</summary>
    private static string NewToken() => "HW-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
}
