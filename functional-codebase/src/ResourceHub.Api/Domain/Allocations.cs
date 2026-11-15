// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the hardware assignment and voucher allocation entities (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

namespace ResourceHub.Api.Domain;

// One physical device handed out by an allocation, identified by its token.
public class HardwareAssignment
{
    public Guid Id { get; set; }
    public required string Token { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid TicketId { get; set; }

    public DateTimeOffset CheckedOutAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public Guid? ReturnedById { get; set; }

    public bool IsOverdue(DateTimeOffset now) => ReturnedAt is null && DueAt < now;
}

// Credit deducted from a voucher asset by an allocation.
public class VoucherAllocation
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public Guid TicketId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset AllocatedAt { get; set; }
}
