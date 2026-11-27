// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: GET /api/v1/assets/allocations with row-level filtering (faculty see only their own courses and labs) and license keys decrypted only for admins and the allocation's requester (CWE-312, CWE-862) (~100%)
// Human Modifications: None yet
// Verification: ActiveAllocationTests, LicenseKeyProtectionTests, ReturnTests
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Endpoints;

public static class AllocationEndpoints
{
    public static void MapAllocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/assets/allocations", ListActiveAsync);
    }

    /// <summary>
    /// Active means (spec 6.12): physical devices not yet returned, and license and voucher
    /// allocations whose ticket is not closed.
    /// Row-level rule: admins see every course and lab; a faculty member sees only rows for the
    /// course or lab they are associated with (another group's query returns no rows); any other
    /// role gets 403. License key values are decrypted only for admins and for the requester of
    /// the allocation's ticket; device check-in codes are never listed.
    /// </summary>
    private static async Task<IResult> ListActiveAsync(
        HttpContext http, HubDbContext db, FieldEncryptor encryptor, TimeProvider clock, CancellationToken ct,
        Guid? courseId = null, Guid? researchLabId = null)
    {
        var caller = await Callers.ResolveAsync(http, db, ct);
        if (Callers.RequireRole(caller, Roles.Admin, Roles.Faculty) is { } denied)
        {
            return denied;
        }

        if ((courseId is null) == (researchLabId is null))
        {
            return Problems.BadRequest("courseId", "Give exactly one of courseId or researchLabId.");
        }
        var exists = courseId is not null
            ? await db.Courses.AnyAsync(c => c.Id == courseId, ct)
            : await db.ResearchLabs.AnyAsync(l => l.Id == researchLabId, ct);
        if (!exists)
        {
            return Problems.BadRequest(courseId is not null ? "courseId" : "researchLabId", "Unknown course or research lab.");
        }

        var mayReadGroup = caller.IsAdmin || caller.IsAssociatedWith(courseId, researchLabId);
        var rows = !mayReadGroup
            ? []
            : await db.Allocations.AsNoTracking()
                .Include(a => a.Asset)
                .Include(a => a.Ticket)
                .Include(a => a.Devices.Where(d => d.ReturnedAt == null))
                .Include(a => a.LicenseKeys)
                .Where(a => courseId != null ? a.Ticket!.CourseId == courseId : a.Ticket!.ResearchLabId == researchLabId)
                .Where(a => a.AssetType == AssetType.Physical
                    ? a.Devices.Any(d => d.ReturnedAt == null)
                    : a.Ticket!.Status != TicketStatus.Closed)
                .OrderByDescending(a => a.AllocatedAt).ThenBy(a => a.Id)
                .AsSplitQuery()
                .ToListAsync(ct);

        var now = clock.GetUtcNow();
        var allocations = rows.Select(a => new AllocationDto(
            a.Id, a.TicketId, a.AssetId, a.Asset!.Name, a.AssetType, a.Asset.Category, a.AllocatedAt,
            a.Quantity, a.Amount,
            a.AssetType == AssetType.License && MaySeeKeys(caller, a)
                ? a.LicenseKeys.Select(k => encryptor.Decrypt(k.KeyCiphertext, k.Id)).Order(StringComparer.Ordinal).ToList()
                : null,
            a.AssetType == AssetType.Physical
                ? a.Devices.OrderBy(d => d.Token, StringComparer.Ordinal)
                    .Select(d => new DeviceDto(d.Token, d.CheckedOutAt, d.DueAt, null, d.DueAt < now ? "overdue" : "checkedout"))
                    .ToList()
                : null)).ToList();

        var devices = allocations.SelectMany(a => a.Devices ?? []).ToList();
        var summary = new AllocationSummaryDto(
            DevicesCheckedOut: devices.Count,
            DevicesOverdue: devices.Count(d => d.Status == "overdue"),
            LicenseKeysAssigned: rows.Where(a => a.AssetType == AssetType.License).Sum(a => a.LicenseKeys.Count),
            VoucherCreditsAllocated: allocations.Where(a => a.AssetType == AssetType.Voucher).Sum(a => a.Amount ?? 0m));

        return Results.Ok(new ActiveAllocationsDto(courseId, researchLabId, summary, allocations));
    }

    /// <summary>Sensitive-field rule: admins, and the requester of the ticket the key was issued to.</summary>
    private static bool MaySeeKeys(Caller caller, Allocation allocation) =>
        caller.IsAdmin || allocation.Ticket!.RequesterId == caller.Id;
}
