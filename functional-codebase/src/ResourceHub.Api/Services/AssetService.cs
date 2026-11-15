// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the asset catalog logic: create, read, update, delete, filters and availability (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: Medium

using Microsoft.EntityFrameworkCore;
using Npgsql;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

public sealed class AssetService
{
    private readonly HubDbContext db;
    private readonly TimeProvider clock;
    private readonly ILogger<AssetService> logger;

    public AssetService(HubDbContext db, TimeProvider clock, ILogger<AssetService> logger)
    {
        this.db = db;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<PagedResponse<AssetResponse>> ListAsync(AssetQuery q, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        IQueryable<Asset> query = db.Assets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            var category = q.Category.Trim().ToLowerInvariant();
            query = query.Where(a => a.Category.ToLower() == category);
        }
        if (!string.IsNullOrWhiteSpace(q.DepartmentProgram))
        {
            var program = q.DepartmentProgram.Trim().ToLowerInvariant();
            query = query.Where(a => a.DepartmentProgram.ToLower() == program);
        }
        if (q.Type is { } type)
        {
            query = query.Where(a => a.Type == type);
        }
        if (q.Availability is { } flag)
        {
            query = flag switch
            {
                AvailabilityFlag.Available => query.Where(a =>
                    (a.Type == AssetType.Physical && a.AvailableUnits > 0)
                    || (a.Type == AssetType.License && a.LicenseKeys.Any(k => k.IssuedAt == null))
                    || (a.Type == AssetType.Voucher && a.Balance > 0)),
                AvailabilityFlag.Reserved => query.Where(a =>
                    a.Type == AssetType.Physical
                    && db.TicketItems.Any(i => i.AssetId == a.Id
                        && db.Tickets.Any(t => t.Id == i.TicketId && t.Status == TicketStatus.Approved))),
                AvailabilityFlag.Checkedout => query.Where(a =>
                    db.HardwareAssignments.Any(h => h.AssetId == a.Id && h.ReturnedAt == null && h.DueAt >= now)),
                AvailabilityFlag.Overdue => query.Where(a =>
                    db.HardwareAssignments.Any(h => h.AssetId == a.Id && h.ReturnedAt == null && h.DueAt < now)),
                _ => query,
            };
        }

        var total = await query.CountAsync(ct);
        var page = await query
            .OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .ToListAsync(ct);

        var items = await ToResponsesAsync(page, includeKeys: false, ct);
        return new PagedResponse<AssetResponse>(items, q.Page, q.PageSize, total);
    }

    public async Task<Result<AssetResponse>> GetAsync(Guid id, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
        {
            return ServiceError.NotFound("Asset");
        }
        return (await ToResponsesAsync([asset], includeKeys: true, ct))[0];
    }

    public async Task<Result<AssetResponse>> CreateAsync(CreateAssetRequest req, CancellationToken ct)
    {
        var type = req.Type!.Value;
        var error = CheckFieldsForType(type, req.TotalUnits, req.DefaultLoanDays, req.TotalCredit,
            hasKeys: req.LicenseKeys is not null, requireAll: true);
        if (error is not null)
        {
            return error;
        }

        var now = clock.GetUtcNow();
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Type = type,
            Category = req.Category.Trim(),
            DepartmentProgram = req.DepartmentProgram.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        switch (type)
        {
            case AssetType.Physical:
                asset.TotalUnits = req.TotalUnits;
                asset.AvailableUnits = req.TotalUnits;
                asset.DefaultLoanDays = req.DefaultLoanDays;
                break;
            case AssetType.Voucher:
                asset.TotalCredit = req.TotalCredit;
                asset.Balance = req.TotalCredit;
                break;
            case AssetType.License:
                var keys = await NewKeysAsync(asset.Id, req.LicenseKeys ?? [], ct);
                if (keys.Error is not null)
                {
                    return keys.Error;
                }
                asset.LicenseKeys.AddRange(keys.Value!);
                break;
        }

        db.Assets.Add(asset);
        var saveError = await SaveAsync(ct);
        if (saveError is not null)
        {
            return saveError;
        }

        logger.LogInformation("Asset {AssetId} ({Type}) created", asset.Id, asset.Type);
        return (await ToResponsesAsync([asset], includeKeys: true, ct))[0];
    }

    public async Task<Result<AssetResponse>> UpdateAsync(Guid id, UpdateAssetRequest req, CancellationToken ct)
    {
        var asset = await db.Assets.Include(a => a.LicenseKeys).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
        {
            return ServiceError.NotFound("Asset");
        }

        var hasKeyChanges = req.AddLicenseKeys is not null || req.RemoveLicenseKeyIds is not null;
        var error = CheckFieldsForType(asset.Type, req.TotalUnits, req.DefaultLoanDays, req.TotalCredit,
            hasKeys: hasKeyChanges, requireAll: true);
        if (error is not null)
        {
            return error;
        }

        asset.Name = req.Name.Trim();
        asset.Category = req.Category.Trim();
        asset.DepartmentProgram = req.DepartmentProgram.Trim();

        switch (asset.Type)
        {
            case AssetType.Physical:
            {
                // Remaining stock moves with the total; it can never drop below zero.
                var newAvailable = asset.AvailableUnits!.Value + (req.TotalUnits!.Value - asset.TotalUnits!.Value);
                if (newAvailable < 0)
                {
                    var outUnits = asset.TotalUnits.Value - asset.AvailableUnits.Value;
                    return ServiceError.Conflict(
                        $"Total units cannot go below the {outUnits} unit(s) currently checked out.");
                }
                asset.TotalUnits = req.TotalUnits;
                asset.AvailableUnits = newAvailable;
                asset.DefaultLoanDays = req.DefaultLoanDays;
                break;
            }
            case AssetType.Voucher:
            {
                var newBalance = asset.Balance!.Value + (req.TotalCredit!.Value - asset.TotalCredit!.Value);
                if (newBalance < 0)
                {
                    var allocated = asset.TotalCredit.Value - asset.Balance.Value;
                    return ServiceError.Conflict(
                        $"Total credit cannot go below the {allocated:0.00} already allocated.");
                }
                asset.TotalCredit = req.TotalCredit;
                asset.Balance = newBalance;
                break;
            }
            case AssetType.License:
            {
                foreach (var keyId in (req.RemoveLicenseKeyIds ?? []).Distinct())
                {
                    var key = asset.LicenseKeys.FirstOrDefault(k => k.Id == keyId);
                    if (key is null)
                    {
                        return ServiceError.BadRequest($"License key {keyId} does not belong to this asset.");
                    }
                    if (key.IsIssued)
                    {
                        return ServiceError.Conflict($"License key {keyId} has been issued and cannot be removed.");
                    }
                    db.LicenseKeys.Remove(key);
                }

                var keys = await NewKeysAsync(asset.Id, req.AddLicenseKeys ?? [], ct);
                if (keys.Error is not null)
                {
                    return keys.Error;
                }
                // Added through the DbSet so EF inserts them rather than treating them as existing rows.
                db.LicenseKeys.AddRange(keys.Value!);
                break;
            }
        }

        asset.UpdatedAt = clock.GetUtcNow();
        var saveError = await SaveAsync(ct);
        if (saveError is not null)
        {
            return saveError;
        }

        logger.LogInformation("Asset {AssetId} updated", asset.Id);
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    public async Task<Result<Done>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
        {
            return ServiceError.NotFound("Asset");
        }

        var inUse = await db.TicketItems.AnyAsync(i => i.AssetId == id, ct)
            || await db.HardwareAssignments.AnyAsync(h => h.AssetId == id, ct)
            || await db.VoucherAllocations.AnyAsync(v => v.AssetId == id, ct)
            || await db.LicenseKeys.AnyAsync(k => k.AssetId == id && k.IssuedAt != null, ct);
        if (inUse)
        {
            return ServiceError.Conflict("The asset is referenced by tickets or allocations and cannot be deleted.");
        }

        db.Assets.Remove(asset);
        var saveError = await SaveAsync(ct);
        if (saveError is not null)
        {
            return saveError;
        }

        logger.LogInformation("Asset {AssetId} deleted", id);
        return Done.Value;
    }

    // Each type has its own stock fields; the others must be left out.
    private static ServiceError? CheckFieldsForType(
        AssetType type, int? totalUnits, int? loanDays, decimal? totalCredit, bool hasKeys, bool requireAll)
    {
        if (!Enum.IsDefined(type))
        {
            return ServiceError.BadRequest("Unknown asset type.");
        }

        switch (type)
        {
            case AssetType.Physical:
                if (totalCredit is not null || hasKeys)
                {
                    return ServiceError.BadRequest("Physical assets take totalUnits and defaultLoanDays only.");
                }
                if (requireAll && (totalUnits is null || loanDays is null))
                {
                    return ServiceError.BadRequest("Physical assets need totalUnits and defaultLoanDays.");
                }
                break;
            case AssetType.License:
                if (totalUnits is not null || loanDays is not null || totalCredit is not null)
                {
                    return ServiceError.BadRequest("License assets take license keys only.");
                }
                break;
            case AssetType.Voucher:
                if (totalUnits is not null || loanDays is not null || hasKeys)
                {
                    return ServiceError.BadRequest("Voucher assets take totalCredit only.");
                }
                if (requireAll && totalCredit is null)
                {
                    return ServiceError.BadRequest("Voucher assets need totalCredit.");
                }
                if (totalCredit is { } credit && decimal.Round(credit, 2) != credit)
                {
                    return ServiceError.BadRequest("totalCredit can have at most two decimal places.");
                }
                break;
        }
        return null;
    }

    private async Task<Result<List<LicenseKey>>> NewKeysAsync(Guid assetId, List<string> values, CancellationToken ct)
    {
        var trimmed = values.Select(v => v?.Trim() ?? string.Empty).ToList();
        if (trimmed.Any(v => v.Length == 0 || v.Length > Limits.MaxKeyLength || v.Any(char.IsControl)))
        {
            return ServiceError.BadRequest(
                $"License keys must be 1 to {Limits.MaxKeyLength} characters with no control characters.");
        }
        if (trimmed.Distinct(StringComparer.Ordinal).Count() != trimmed.Count)
        {
            return ServiceError.BadRequest("The same license key appears more than once.");
        }
        if (trimmed.Count > 0 && await db.LicenseKeys.AnyAsync(k => trimmed.Contains(k.Value), ct))
        {
            // Deliberately does not say which key, so the API cannot be used to probe for key values.
            return ServiceError.Conflict("One or more license keys already exist.");
        }

        return trimmed.Select(v => new LicenseKey { Id = Guid.NewGuid(), AssetId = assetId, Value = v }).ToList();
    }

    private async Task<ServiceError?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceError.Conflict("The asset was changed by another request. Read it again and retry.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ServiceError.Conflict("One or more license keys already exist.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return ServiceError.Conflict("The asset is referenced by tickets or allocations.");
        }
    }

    // Adds the availability figures, computed in three grouped queries for the whole page.
    private async Task<List<AssetResponse>> ToResponsesAsync(List<Asset> assets, bool includeKeys, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ids = assets.Select(a => a.Id).ToList();

        var keyCounts = await db.LicenseKeys.AsNoTracking()
            .Where(k => ids.Contains(k.AssetId))
            .GroupBy(k => k.AssetId)
            .Select(g => new { AssetId = g.Key, Total = g.Count(), Issued = g.Count(k => k.IssuedAt != null) })
            .ToDictionaryAsync(x => x.AssetId, ct);

        var reserved = await (
                from i in db.TicketItems
                join t in db.Tickets on i.TicketId equals t.Id
                where t.Status == TicketStatus.Approved && ids.Contains(i.AssetId) && i.Quantity != null
                group i by i.AssetId into g
                select new { AssetId = g.Key, Units = g.Sum(i => i.Quantity!.Value) })
            .ToDictionaryAsync(x => x.AssetId, x => x.Units, ct);

        var devices = await db.HardwareAssignments.AsNoTracking()
            .Where(h => ids.Contains(h.AssetId) && h.ReturnedAt == null)
            .GroupBy(h => h.AssetId)
            .Select(g => new
            {
                AssetId = g.Key,
                Out = g.Count(h => h.DueAt >= now),
                Overdue = g.Count(h => h.DueAt < now),
            })
            .ToDictionaryAsync(x => x.AssetId, ct);

        Dictionary<Guid, List<MaskedLicenseKey>> masked = [];
        if (includeKeys)
        {
            var keys = await db.LicenseKeys.AsNoTracking()
                .Where(k => ids.Contains(k.AssetId))
                .OrderBy(k => k.Id)
                .ToListAsync(ct);
            masked = keys.GroupBy(k => k.AssetId)
                .ToDictionary(g => g.Key, g => g.Select(k => new MaskedLicenseKey(k.Id, k.MaskedValue, k.IsIssued)).ToList());
        }

        return assets.Select(a =>
        {
            decimal total, remaining;
            PhysicalAvailability? physical = null;
            LicenseAvailability? license = null;
            VoucherAvailability? voucher = null;
            IReadOnlyList<MaskedLicenseKey>? keyList = null;

            switch (a.Type)
            {
                case AssetType.Physical:
                    var d = devices.GetValueOrDefault(a.Id);
                    total = a.TotalUnits ?? 0;
                    remaining = a.AvailableUnits ?? 0;
                    physical = new PhysicalAvailability(
                        a.AvailableUnits ?? 0, reserved.GetValueOrDefault(a.Id), d?.Out ?? 0, d?.Overdue ?? 0);
                    break;
                case AssetType.License:
                    var k = keyCounts.GetValueOrDefault(a.Id);
                    total = k?.Total ?? 0;
                    remaining = (k?.Total ?? 0) - (k?.Issued ?? 0);
                    license = new LicenseAvailability((int)remaining, k?.Issued ?? 0);
                    if (includeKeys)
                    {
                        keyList = masked.GetValueOrDefault(a.Id) ?? [];
                    }
                    break;
                default:
                    total = a.TotalCredit ?? 0;
                    remaining = a.Balance ?? 0;
                    voucher = new VoucherAvailability(remaining, total - remaining);
                    break;
            }

            return new AssetResponse(a.Id, a.Name, a.Type, a.Category, a.DepartmentProgram, total, remaining,
                remaining > 0, a.DefaultLoanDays, physical, license, voucher, keyList, a.CreatedAt, a.UpdatedAt);
        }).ToList();
    }
}
