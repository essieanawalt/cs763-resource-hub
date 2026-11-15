// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: /api/v1/assets routes: create, list with filters, read, update, delete (~100%)
// Human Modifications: None yet
// Verification: SeedDataTests, AssetCrudTests, AssetQueryTests
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var assets = app.MapGroup("/api/v1/assets");

        assets.MapPost("/", CreateAsync);
        assets.MapGet("/", ListAsync);
        assets.MapGet("/{assetId:guid}", GetAsync);
        assets.MapPut("/{assetId:guid}", UpdateAsync);
        assets.MapDelete("/{assetId:guid}", DeleteAsync);
        assets.MapPost("/{assetId:guid}/return", ReturnAsync);
    }

    /// <summary>One device per call, identified by its hardware assignment token (spec 6.11).</summary>
    private static async Task<IResult> ReturnAsync(
        Guid assetId, ReturnRequest body, HttpContext http, HubDbContext db, TimeProvider clock, ILogger<ReturnRequest> log, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct, UserRole.Administrator);
        if (denied is not null)
        {
            return denied;
        }

        var errors = new Errors();
        var token = Validation.Text(errors, "token", body.Token, 40);
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

        var now = clock.GetUtcNow();
        device.ReturnedAt = now;
        asset.RemainingUnits += 1;
        asset.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var wasOverdue = now > device.DueAt;
        log.LogInformation("Device {Token} of asset {AssetId} returned by {ActorId} (overdue: {Overdue})", token, assetId, actor!.Id, wasOverdue);

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
        AssetRequest body, HttpContext http, HubDbContext db, TimeProvider clock, ILogger<AssetRequest> log, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct, UserRole.Administrator);
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
                asset.LicenseKeys = keys.Select(k => new LicenseKey { Id = Guid.NewGuid(), AssetId = asset.Id, Key = k }).ToList();
                break;
            case AssetType.Voucher:
                asset.TotalBalance = asset.Balance = body.Balance;
                break;
        }

        db.Assets.Add(asset);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Asset {AssetId} ({Type}) created by {ActorId}", asset.Id, asset.Type, actor!.Id);

        return Results.Created($"/api/v1/assets/{asset.Id}", await AssetViews.ToDtoAsync(db, asset, now, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid assetId, AssetRequest body, HttpContext http, HubDbContext db, TimeProvider clock, ILogger<AssetRequest> log, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct, UserRole.Administrator);
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
                var wanted = keys.ToHashSet(StringComparer.Ordinal);
                var missingIssued = asset.LicenseKeys.Where(k => k.IssuedTicketId is not null && !wanted.Contains(k.Key)).ToList();
                if (missingIssued.Count > 0)
                {
                    return Problems.Conflict("Issued license keys cannot be removed.",
                        $"{missingIssued.Count} of the keys left out have already been issued.");
                }
                db.LicenseKeys.RemoveRange(asset.LicenseKeys.Where(k => k.IssuedTicketId is null && !wanted.Contains(k.Key)));
                var existing = asset.LicenseKeys.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var key in keys.Where(k => !existing.Contains(k)))
                {
                    db.LicenseKeys.Add(new LicenseKey { Id = Guid.NewGuid(), AssetId = asset.Id, Key = key });
                }
                var issued = asset.LicenseKeys.Count(k => k.IssuedTicketId is not null);
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
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Asset {AssetId} updated by {ActorId}", asset.Id, actor!.Id);

        return Results.Ok(await AssetViews.ToDtoAsync(db, asset, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> DeleteAsync(
        Guid assetId, HttpContext http, HubDbContext db, ILogger<AssetRequest> log, CancellationToken ct)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct, UserRole.Administrator);
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
        await db.SaveChangesAsync(ct);
        log.LogInformation("Asset {AssetId} deleted by {ActorId}", assetId, actor!.Id);
        return Results.NoContent();
    }
}
