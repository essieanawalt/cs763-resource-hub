// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Request and response shapes for /api/v1/assets/tickets (~100%)
// Human Modifications: None yet
// Verification: TicketSubmitTests, TicketQueryTests, TicketLifecycleTests, TicketMetricsTests
// Confidence: High

using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Contracts;

public sealed record TicketItemRequest(Guid? AssetId, int? Quantity, decimal? Amount, int? LoanDays);

/// <summary>
/// POST takes the content fields. PUT takes either the content fields (to edit a pending ticket)
/// or a status (and optional reason) to move the ticket to a new state, not both.
/// </summary>
public sealed record TicketRequest(
    Guid? CourseId,
    Guid? ResearchLabId,
    Guid? ProjectId,
    string? Notes,
    List<TicketItemRequest>? Items,
    TicketStatus? Status,
    string? Reason);

public sealed record TicketItemDto(
    Guid AssetId,
    string AssetName,
    AssetType AssetType,
    string Category,
    int? Quantity,
    decimal? Amount,
    int? LoanDays);

public sealed record TicketEventDto(
    TicketEventType Type,
    TicketStatus? FromStatus,
    TicketStatus? ToStatus,
    Guid? ActorId,
    string? Detail,
    DateTimeOffset OccurredAt);

/// <summary>Timings in seconds between lifecycle steps (spec 6.13); null until both steps happened.</summary>
public sealed record TicketMetricsDto(
    double? SubmittedToDecisionSeconds,
    double? ApprovedToAllocatedSeconds,
    double? SubmittedToAllocatedSeconds,
    double? AllocatedToClosedSeconds,
    int AllocationAttempts,
    int FailedAllocationAttempts);

public sealed record TicketDto(
    Guid Id,
    TicketStatus Status,
    Guid RequesterId,
    Guid? CourseId,
    Guid? ResearchLabId,
    Guid? ProjectId,
    string? Notes,
    IReadOnlyList<TicketItemDto> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    DateTimeOffset? AllocatedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<TicketEventDto>? Events = null,
    TicketMetricsDto? Metrics = null);
