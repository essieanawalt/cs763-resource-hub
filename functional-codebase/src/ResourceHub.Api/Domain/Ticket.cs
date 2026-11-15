// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the ticket, ticket item and ticket event entities (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

namespace ResourceHub.Api.Domain;

public class Ticket
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }

    // Exactly one of these is set.
    public Guid? CourseId { get; set; }
    public Guid? ResearchLabId { get; set; }

    public Guid? ProjectId { get; set; }
    public string? Notes { get; set; }
    public TicketStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Set by the IT director's approve or reject.
    public DateTimeOffset? SignedOffAt { get; set; }
    public Guid? SignedOffById { get; set; }

    public DateTimeOffset? AllocatedAt { get; set; }
    public Guid? AllocatedById { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public uint Version { get; set; }

    public List<TicketItem> Items { get; set; } = [];
    public List<TicketEvent> Events { get; set; } = [];
}

// One requested asset. Physical and license items use Quantity; voucher items use Amount.
public class TicketItem
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }

    public int? Quantity { get; set; }
    public decimal? Amount { get; set; }

    // Physical only; overrides the asset's default loan period.
    public int? LoanDays { get; set; }
}

// Logging metrics: one row per state change and per allocation attempt (spec 6.13).
public class TicketEvent
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public TicketEventType Type { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Detail { get; set; }
}
