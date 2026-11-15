// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Factory for ticket lifecycle events (spec 6.13) (~100%)
// Human Modifications: None yet
// Verification: TicketSubmitTests, TicketLifecycleTests, AllocationTests, TicketMetricsTests
// Confidence: High

using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

public static class TicketEvents
{
    public static TicketEvent Create(
        Guid ticketId, TicketEventType type, TicketStatus? from, TicketStatus? to, Guid? actorId, DateTimeOffset at, string? detail = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Type = type,
            FromStatus = from,
            ToStatus = to,
            ActorId = actorId,
            Detail = detail is { Length: > 2000 } ? detail[..2000] : detail,
            OccurredAt = at,
        };
}
