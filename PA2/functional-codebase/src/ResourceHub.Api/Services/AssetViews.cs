// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Builds asset responses with availability counts by status flag (~100%)
// Human Modifications: None (review only)
// Verification: SeedDataTests, AssetCrudTests, AssetQueryTests, ReturnTests; reviewed by me 2026-11-15: could use a second pass for null-checks
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

public static class AssetViews
{
    /// <summary>Builds DTOs for a set of assets with two grouped queries for the status counts.</summary>
    public static async Task<List<AssetDto>> ToDtosAsync(
        HubDbContext db, IReadOnlyList<Asset> assets, DateTimeOffset now, CancellationToken ct)
    {
        var physicalIds = assets.Where(a => a.Type == AssetType.Physical).Select(a => a.Id).ToList();

        // Reserved: units requested on approved tickets that have not been allocated yet.
        var reserved = await db.TicketItems
            .Where(i => physicalIds.Contains(i.AssetId)
                        && db.Tickets.Any(t => t.Id == i.TicketId && t.Status == TicketStatus.Approved))
            .GroupBy(i => i.AssetId)
            .Select(g => new { AssetId = g.Key, Units = g.Sum(i => i.Quantity ?? 0) })
            .ToDictionaryAsync(x => x.AssetId, x => x.Units, ct);

        var outstanding = await db.DeviceAssignments
            .Where(d => physicalIds.Contains(d.AssetId) && d.ReturnedAt == null)
            .GroupBy(d => d.AssetId)
            .Select(g => new
            {
                AssetId = g.Key,
                Overdue = g.Count(d => d.DueAt < now),
                CheckedOut = g.Count(d => d.DueAt >= now),
            })
            .ToDictionaryAsync(x => x.AssetId, ct);

        return assets.Select(a =>
        {
            var counts = outstanding.GetValueOrDefault(a.Id);
            return ToDto(a, reserved.GetValueOrDefault(a.Id), counts?.CheckedOut ?? 0, counts?.Overdue ?? 0);
        }).ToList();
    }

    public static async Task<AssetDto> ToDtoAsync(HubDbContext db, Asset asset, DateTimeOffset now, CancellationToken ct) =>
        (await ToDtosAsync(db, [asset], now, ct))[0];

    private static AssetDto ToDto(Asset a, int reserved, int checkedOut, int overdue)
    {
        var (total, remaining, availability) = a.Type switch
        {
            AssetType.Physical => (
                (decimal)(a.TotalUnits ?? 0),
                (decimal)(a.RemainingUnits ?? 0),
                new AvailabilityDto(
                    IsAvailable: a.RemainingUnits > 0,
                    Available: a.RemainingUnits ?? 0,
                    Reserved: reserved,
                    Checkedout: checkedOut,
                    Overdue: overdue)),
            AssetType.License => (
                (decimal)(a.TotalUnits ?? 0),
                (decimal)(a.RemainingUnits ?? 0),
                new AvailabilityDto(
                    IsAvailable: a.RemainingUnits > 0,
                    Available: a.RemainingUnits ?? 0,
                    Issued: (a.TotalUnits ?? 0) - (a.RemainingUnits ?? 0))),
            _ => (
                a.TotalBalance ?? 0m,
                a.Balance ?? 0m,
                new AvailabilityDto(
                    IsAvailable: a.Balance > 0m,
                    Balance: a.Balance ?? 0m,
                    Allocated: (a.TotalBalance ?? 0m) - (a.Balance ?? 0m))),
        };

        return new AssetDto(
            a.Id, a.Name, a.Type, a.Category, a.DepartmentProgram,
            total, remaining, a.DefaultLoanDays, availability, a.CreatedAt, a.UpdatedAt);
    }
}
