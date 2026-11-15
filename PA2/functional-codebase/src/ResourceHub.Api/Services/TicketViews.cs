// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Builds ticket responses, with events and timing metrics for the detail view (~100%)
// Human Modifications: None (review only)
// Verification: TicketSubmitTests, TicketMetricsTests; reviewed by me 2026-11-15: what is the timezone for the logs
// Confidence: High

using ResourceHub.Api.Contracts;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

public static class TicketViews
{
    /// <summary>Needs Items with Asset loaded; Events only when <paramref name="detail"/> is true.</summary>
    public static TicketDto ToDto(Ticket t, bool detail = false)
    {
        var items = t.Items
            .OrderBy(i => i.Asset!.Name).ThenBy(i => i.AssetId)
            .Select(i => new TicketItemDto(i.AssetId, i.Asset!.Name, i.Asset.Type, i.Asset.Category, i.Quantity, i.Amount, i.LoanDays))
            .ToList();

        List<TicketEventDto>? events = null;
        TicketMetricsDto? metrics = null;
        if (detail)
        {
            var ordered = t.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Type).ToList();
            events = ordered
                .Select(e => new TicketEventDto(e.Type, e.FromStatus, e.ToStatus, e.ActorId, e.Detail, e.OccurredAt))
                .ToList();
            var decidedAt = t.ApprovedAt ?? t.RejectedAt;
            metrics = new TicketMetricsDto(
                SubmittedToDecisionSeconds: Seconds(t.CreatedAt, decidedAt),
                ApprovedToAllocatedSeconds: Seconds(t.ApprovedAt, t.AllocatedAt),
                SubmittedToAllocatedSeconds: Seconds(t.CreatedAt, t.AllocatedAt),
                AllocatedToClosedSeconds: Seconds(t.AllocatedAt, t.ClosedAt),
                AllocationAttempts: ordered.Count(e => e.Type is TicketEventType.AllocationSucceeded or TicketEventType.AllocationFailed),
                FailedAllocationAttempts: ordered.Count(e => e.Type == TicketEventType.AllocationFailed));
        }

        return new TicketDto(
            t.Id, t.Status, t.RequesterId, t.CourseId, t.ResearchLabId, t.ProjectId, t.Notes, items,
            t.CreatedAt, t.UpdatedAt, t.ApprovedAt, t.RejectedAt, t.AllocatedAt, t.ClosedAt,
            events, metrics);
    }

    private static double? Seconds(DateTimeOffset? from, DateTimeOffset? to) =>
        from is null || to is null ? null : (to.Value - from.Value).TotalSeconds;
}
