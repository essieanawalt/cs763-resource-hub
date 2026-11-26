// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: The allocation engine: atomic, all-or-nothing deductions that issue keys, credits and device tokens; PA3 decrypts issued keys only for the admin response (CWE-312), issues device check-in codes (T1) and audits stock changes (T2) (~100%)
// Human Modifications: None yet
// Verification: AllocationTests, LicenseKeyProtectionTests, ReturnTests, AuditTrailTests
// Confidence: High

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Security;

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
    private readonly FieldEncryptor encryptor;
    private readonly AuditTrail audit;

    public AllocationService(HubDbContext db, TimeProvider clock, ILogger<AllocationService> log, FieldEncryptor encryptor, AuditTrail audit)
    {
        this.db = db;
        this.clock = clock;
        this.log = log;
        this.encryptor = encryptor;
        this.audit = audit;
    }

    /// <summary>Admin only (checked by the endpoint). The response carries decrypted keys and check-in codes for the admin.</summary>
    public async Task<IResult> AllocateAsync(Guid ticketId, Caller actor, CancellationToken ct)
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
                            var checkInCode = CheckInCodes.New();
                            db.DeviceAssignments.Add(new DeviceAssignment
                            {
                                Id = Guid.NewGuid(),
                                Token = token,
                                AllocationId = allocation.Id,
                                AssetId = asset.Id,
                                TicketId = ticketId,
                                CheckedOutAt = now,
                                DueAt = due,
                                CheckInCodeHash = CheckInCodes.Hash(checkInCode),
                            });
                            devices.Add(new DeviceDto(token, now, due, null, "checkedout", checkInCode));
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
                        // Decrypted only for this admin response; stored values stay ciphertext.
                        keys = free.Select(k => encryptor.Decrypt(k.KeyCiphertext, k.Id)).ToList();
                        break;
                    }
                case AssetType.Voucher:
                    asset.Balance -= item.Amount!.Value;
                    break;
            }
            asset.UpdatedAt = now;
            audit.Record(actor, "asset.stock", "asset", asset.Id, detail: asset.Type switch
            {
                AssetType.Voucher => $"{item.Amount} credits allocated to ticket {ticketId}",
                _ => $"{item.Quantity} unit(s) allocated to ticket {ticketId}",
            });

            results.Add(new AllocationDto(
                allocation.Id, ticketId, asset.Id, asset.Name, asset.Type, asset.Category, now,
                allocation.Quantity, allocation.Amount, keys, devices));
        }

        ticket.Status = TicketStatus.Allocated;
        ticket.AllocatedAt = now;
        ticket.UpdatedAt = now;
        db.TicketEvents.Add(TicketEvents.Create(ticketId, TicketEventType.AllocationSucceeded,
            TicketStatus.Approved, TicketStatus.Allocated, actor.Id, now, $"{results.Count} asset(s) allocated"));
        audit.Record(actor, "ticket.allocate", "ticket", ticketId, detail: $"{results.Count} asset(s) allocated");

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Ticket {TicketId} allocated by {Sub}: {AssetCount} asset(s), {DeviceCount} device(s), {KeyCount} key(s), {Credits} credits",
            ticketId, actor.Sub, results.Count,
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
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Ticket ticket, Caller actor, DateTimeOffset now, string detail, CancellationToken ct)
    {
        await tx.RollbackAsync(ct);
        db.ChangeTracker.Clear();
        db.TicketEvents.Add(TicketEvents.Create(ticket.Id, TicketEventType.AllocationFailed, ticket.Status, ticket.Status, actor.Id, now, detail));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Hardware assignment token: unguessable, so it can identify a device at return.</summary>
    private static string NewToken() => "HW-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
}
