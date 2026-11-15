// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: GET /api/v1/assets/allocations: active allocations and metrics for one course or lab (~100%)
// Human Modifications: None (review only)
// Verification: ActiveAllocationTests; reviewed by me 2026-11-15: lists only certain roles can view things, but roles can be spoofed
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;
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
    /// allocations whose ticket is not closed. Issued license keys are shown, so only the
    /// group's own requester role, the IT director and administrators may read this.
    /// </summary>
    private static async Task<IResult> ListActiveAsync(
        HttpContext http, HubDbContext db, TimeProvider clock, CancellationToken ct,
        Guid? courseId = null, Guid? researchLabId = null)
    {
        var (actor, denied) = await Actors.RequireAsync(http, db, ct);
        if (denied is not null)
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

        var allowed = actor!.Role switch
        {
            UserRole.Administrator or UserRole.ItDirector => true,
            UserRole.CourseInstructor => courseId is not null && actor.CourseId == courseId,
            UserRole.LabAdvisor => researchLabId is not null && actor.ResearchLabId == researchLabId,
            _ => false,
        };
        if (!allowed)
        {
            return Problems.Forbidden("Only the course's instructor, the lab's advisor, the IT director or an administrator can read these allocations.");
        }

        var rows = await db.Allocations.AsNoTracking()
            .Include(a => a.Asset)
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
            a.AssetType == AssetType.License ? a.LicenseKeys.Select(k => k.Key).Order(StringComparer.Ordinal).ToList() : null,
            a.AssetType == AssetType.Physical
                ? a.Devices.OrderBy(d => d.Token, StringComparer.Ordinal)
                    .Select(d => new DeviceDto(d.Token, d.CheckedOutAt, d.DueAt, null, d.DueAt < now ? "overdue" : "checkedout"))
                    .ToList()
                : null)).ToList();

        var devices = allocations.SelectMany(a => a.Devices ?? []).ToList();
        var summary = new AllocationSummaryDto(
            DevicesCheckedOut: devices.Count,
            DevicesOverdue: devices.Count(d => d.Status == "overdue"),
            LicenseKeysAssigned: allocations.Sum(a => a.LicenseKeys?.Count ?? 0),
            VoucherCreditsAllocated: allocations.Where(a => a.AssetType == AssetType.Voucher).Sum(a => a.Amount ?? 0m));

        return Results.Ok(new ActiveAllocationsDto(courseId, researchLabId, summary, allocations));
    }
}
