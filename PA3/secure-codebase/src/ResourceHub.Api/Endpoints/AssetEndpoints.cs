// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Asset catalog (any authenticated caller) and admin-only writes and returns; keys encrypted before storage (CWE-312); returns need the check-in code (T1); no device token in logs (CWE-532); audited changes (T2); log lines through the generated HubLog methods (CA1873) (~100%)
// Human Modifications: None yet
// Verification: AssetCrudTests, AssetQueryTests, ReturnTests, LicenseKeyProtectionTests, AuditTrailTests (all run as the runtime database role); dotnet build with CA1873 as a warning
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Endpoints;

public static class AssetEndpoints
{
    private const string Entity = "asset";

    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var assets = app.MapGroup("/api/v1/assets");

        assets.MapPost("/", CreateAsync).Audited("asset.create", Entity);
        assets.MapGet("/", ListAsync);
        assets.MapGet("/{assetId:guid}", GetAsync);
        assets.MapPut("/{assetId:guid}", UpdateAsync).Audited("asset.update", Entity, "assetId");
        assets.MapDelete("/{assetId:guid}", DeleteAsync).Audited("asset.delete", Entity, "assetId");
        assets.MapPost("/{assetId:guid}/return", ReturnAsync).Audited("device.return", Entity, "assetId");
    }

    private static async Task<(Caller Caller, IResult? Denied)> AdminAsync(HttpContext http, HubDbContext db, CancellationToken ct)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);
        return (caller, Callers.RequireRole(caller, Roles.Admin));
    }

    /// <summary>
    /// One device per call (spec 6.11). A return must prove three things (T1): the caller is an
    /// admin (verified token), the token names a device of this asset that is still out, and the
    /// check-in code from the device's label matches the hash stored at hand-out. The token alone,
    /// which the borrower can see, is not enough.
    /// </summary>
    private static async Task<IResult> ReturnAsync(
        Guid assetId, ReturnRequest body, HttpContext http, HubDbContext db, AuditTrail audit, TimeProvider clock,
        ILogger<ReturnRequest> log, CancellationToken ct)
    {
        var (caller, denied) = await AdminAsync(http, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        var errors = new Errors();
        var token = Validation.DeviceCode(errors, "token", body.Token);
        var checkInCode = Validation.DeviceCode(errors, "checkInCode", body.CheckInCode);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var asset = await db.Assets.FromSql($"SELECT * FROM assets WHERE \"Id\" = {assetId} FOR UPDATE").FirstOrDefaultAsync(ct);
        if (asset is null)
        {
            return Problems.NotFound("Asset not found.");
        }
        if (asset.Type != AssetType.Physical)
        {
            return Problems.BadRequest("assetId", "Only physical devices are returned.");
        }

        var device = await db.DeviceAssignments.FirstOrDefaultAsync(d => d.Token == token && d.AssetId == assetId, ct);
        if (device is null)
        {
            return Problems.NotFound("No device with this token was checked out from this asset.");
        }
        if (device.ReturnedAt is not null)
        {
            return Problems.Conflict("Device already returned.", $"It was returned at {device.ReturnedAt:O}.");
        }
        if (!CheckInCodes.Matches(checkInCode!, device.CheckInCodeHash))
        {
            log.DeviceReturnRefused(device.Id, assetId, caller.Id);
            return Problems.Unprocessable("Check-in code does not match this device.",
                "Read the check-in code from the device's label; the device stays checked out.");
        }

        var now = clock.GetUtcNow();
        device.ReturnedAt = now;
        asset.RemainingUnits += 1;
        asset.UpdatedAt = now;
        var wasOverdue = now > device.DueAt;
        audit.Record(caller, "device.return", Entity, assetId,
            detail: $"Device {device.Id} of ticket {device.TicketId} returned; overdue: {wasOverdue}; stock now {asset.RemainingUnits}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // CWE-532: log the device's internal id, never its token or check-in code.
        log.DeviceReturned(device.Id, assetId, caller.Id, wasOverdue);

        return Results.Ok(new ReturnResultDto(
            device.Token, assetId, device.TicketId, device.CheckedOutAt, device.DueAt, now,
            Math.Round((now - device.CheckedOutAt).TotalHours, 2), wasOverdue, asset.RemainingUnits!.Value));
    }

    /// <summary>Availability states a listing can be filtered by (spec requirement 2 and 6.3).</summary>
    private enum AvailabilityFilter
    {
        Available,
        Reserved,
        Checkedout,
        Overdue,
    }

    /// <summary>Any authenticated caller may read and search the catalog. Search covers names only, never key values.</summary>
    private static async Task<IResult> ListAsync(
        HubDbContext db, TimeProvider clock, CancellationToken ct,
        string? category = null, string? departmentProgram = null, string? availability = null,
        string? type = null, string? search = null, int? page = null, int? pageSize = null)
    {
        var errors = new Errors();
        var state = Query.Enum<AvailabilityFilter>(errors, "availability", availability);
        var assetType = Query.Enum<AssetType>(errors, "type", type);
        var (p, size) = Query.Paging(errors, page, pageSize);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        var now = clock.GetUtcNow();
        var q = db.Assets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = category.Trim().ToLower();
            q = q.Where(a => a.Category.ToLower() == c);
        }
        if (!string.IsNullOrWhiteSpace(departmentProgram))
        {
            var d = departmentProgram.Trim().ToLower();
            q = q.Where(a => a.DepartmentProgram.ToLower() == d);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(a => a.Name.ToLower().Contains(s));
        }
        if (assetType is not null)
        {
            q = q.Where(a => a.Type == assetType);
        }

        q = state switch
        {
            // License and voucher assets count as available while stock or balance remains (spec 6.3).
            AvailabilityFilter.Available => q.Where(a => a.RemainingUnits > 0 || a.Balance > 0),
            AvailabilityFilter.Reserved => q.Where(a => a.Type == AssetType.Physical && db.TicketItems.Any(i =>
                i.AssetId == a.Id && db.Tickets.Any(t => t.Id == i.TicketId && t.Status == TicketStatus.Approved))),
            AvailabilityFilter.Checkedout => q.Where(a => db.DeviceAssignments.Any(d =>
                d.AssetId == a.Id && d.ReturnedAt == null && d.DueAt >= now)),
            AvailabilityFilter.Overdue => q.Where(a => db.DeviceAssignments.Any(d =>
                d.AssetId == a.Id && d.ReturnedAt == null && d.DueAt < now)),
            _ => q,
        };

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<AssetDto>(await AssetViews.ToDtosAsync(db, items, now, ct), p, size, total));
    }

    private static async Task<IResult> GetAsync(Guid assetId, HubDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, ct);
        return asset is null
            ? Problems.NotFound("Asset not found.")
            : Results.Ok(await AssetViews.ToDtoAsync(db, asset, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> CreateAsync(
        AssetRequest body, HttpContext http, HubDbContext db, FieldEncryptor encryptor, AuditTrail audit, TimeProvider clock,
        ILogger<AssetRequest> log, CancellationToken ct)
    {
        var (caller, denied) = await AdminAsync(http, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        var errors = new Errors();
        var (name, category, department, keys) = Validation.Asset(errors, body);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        var now = clock.GetUtcNow();
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = body.Type!.Value,
            Category = category,
            DepartmentProgram = department,
            CreatedAt = now,
            UpdatedAt = now,
        };

        switch (asset.Type)
        {
            case AssetType.Physical:
                asset.TotalUnits = asset.RemainingUnits = body.Units;
                asset.DefaultLoanDays = body.DefaultLoanDays;
                break;
            case AssetType.License:
                asset.TotalUnits = asset.RemainingUnits = keys.Count;
                asset.LicenseKeys = keys.Select(k => NewKey(encryptor, asset.Id, k)).ToList();
                break;
            case AssetType.Voucher:
                asset.TotalBalance = asset.Balance = body.Balance;
                break;
        }

        db.Assets.Add(asset);
        audit.Record(caller, "asset.create", Entity, asset.Id, detail: $"{asset.Type} '{asset.Name}', stock {StockText(asset)}");
        await db.SaveChangesAsync(ct);
        log.AssetCreated(asset.Id, asset.Type, caller.Id);

        return Results.Created($"/api/v1/assets/{asset.Id}", await AssetViews.ToDtoAsync(db, asset, now, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid assetId, AssetRequest body, HttpContext http, HubDbContext db, FieldEncryptor encryptor, AuditTrail audit, TimeProvider clock,
        ILogger<AssetRequest> log, CancellationToken ct)
    {
        var (caller, denied) = await AdminAsync(http, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        var errors = new Errors();
        var (name, category, department, keys) = Validation.Asset(errors, body);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Lock the row so an allocation or return cannot change stock underneath this update.
        var asset = await db.Assets
            .FromSql($"SELECT * FROM assets WHERE \"Id\" = {assetId} FOR UPDATE")
            .Include(a => a.LicenseKeys)
            .FirstOrDefaultAsync(ct);
        if (asset is null)
        {
            return Problems.NotFound("Asset not found.");
        }
        if (asset.Type != body.Type)
        {
            return Problems.BadRequest("type", "An asset's type cannot be changed.");
        }

        var before = StockText(asset);
        switch (asset.Type)
        {
            case AssetType.Physical:
                {
                    var handedOut = asset.TotalUnits!.Value - asset.RemainingUnits!.Value;
                    if (body.Units < handedOut)
                    {
                        return Problems.Conflict("Total stock below units already handed out.",
                            $"{handedOut} units are checked out; total stock cannot go below that.");
                    }
                    asset.TotalUnits = body.Units;
                    asset.RemainingUnits = body.Units - handedOut;
                    asset.DefaultLoanDays = body.DefaultLoanDays;
                    break;
                }
            case AssetType.License:
                {
                    // Stored values are ciphertext; compare on the decrypted values in memory.
                    var stored = asset.LicenseKeys.Select(k => (Row: k, Plain: encryptor.Decrypt(k.KeyCiphertext, k.Id))).ToList();
                    var wanted = keys.ToHashSet(StringComparer.Ordinal);
                    var missingIssued = stored.Where(k => k.Row.IssuedTicketId is not null && !wanted.Contains(k.Plain)).ToList();
                    if (missingIssued.Count > 0)
                    {
                        return Problems.Conflict("Issued license keys cannot be removed.",
                            $"{missingIssued.Count} of the keys left out have already been issued.");
                    }
                    db.LicenseKeys.RemoveRange(stored.Where(k => k.Row.IssuedTicketId is null && !wanted.Contains(k.Plain)).Select(k => k.Row));
                    var existing = stored.Select(k => k.Plain).ToHashSet(StringComparer.Ordinal);
                    foreach (var key in keys.Where(k => !existing.Contains(k)))
                    {
                        db.LicenseKeys.Add(NewKey(encryptor, asset.Id, key));
                    }
                    var issued = stored.Count(k => k.Row.IssuedTicketId is not null);
                    asset.TotalUnits = keys.Count;
                    asset.RemainingUnits = keys.Count - issued;
                    break;
                }
            case AssetType.Voucher:
                {
                    var used = asset.TotalBalance!.Value - asset.Balance!.Value;
                    if (body.Balance < used)
                    {
                        return Problems.Conflict("Total credits below credits already allocated.",
                            $"{used} credits are allocated; the total cannot go below that.");
                    }
                    asset.TotalBalance = body.Balance;
                    asset.Balance = body.Balance - used;
                    break;
                }
        }

        asset.Name = name;
        asset.Category = category;
        asset.DepartmentProgram = department;
        asset.UpdatedAt = clock.GetUtcNow();
        audit.Record(caller, "asset.update", Entity, asset.Id, detail: $"stock {before} -> {StockText(asset)}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.AssetUpdated(asset.Id, caller.Id);

        return Results.Ok(await AssetViews.ToDtoAsync(db, asset, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> DeleteAsync(
        Guid assetId, HttpContext http, HubDbContext db, AuditTrail audit, ILogger<AssetRequest> log, CancellationToken ct)
    {
        var (caller, denied) = await AdminAsync(http, db, ct);
        if (denied is not null)
        {
            return denied;
        }

        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset is null)
        {
            return Problems.NotFound("Asset not found.");
        }

        var referenced = await db.TicketItems.AnyAsync(i => i.AssetId == assetId, ct)
                         || await db.Allocations.AnyAsync(a => a.AssetId == assetId, ct);
        if (referenced)
        {
            return Problems.Conflict("Asset is in use.", "Tickets or allocations refer to this asset, so it cannot be deleted.");
        }

        db.Assets.Remove(asset);
        audit.Record(caller, "asset.delete", Entity, assetId, detail: $"{asset.Type} '{asset.Name}', stock {StockText(asset)}");
        await db.SaveChangesAsync(ct);
        log.AssetDeleted(assetId, caller.Id);
        return Results.NoContent();
    }

    /// <summary>Encrypts a key value bound to its own row id before it is stored (CWE-312).</summary>
    private static LicenseKey NewKey(FieldEncryptor encryptor, Guid assetId, string key)
    {
        var id = Guid.NewGuid();
        return new LicenseKey { Id = id, AssetId = assetId, KeyCiphertext = encryptor.Encrypt(key, id) };
    }

    /// <summary>Counts only: never key values.</summary>
    private static string StockText(Asset a) => a.Type == AssetType.Voucher
        ? $"{a.Balance}/{a.TotalBalance} credits"
        : $"{a.RemainingUnits}/{a.TotalUnits} units";
}
