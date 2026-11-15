// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the ticket and allocation request, query and response contracts (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System.ComponentModel.DataAnnotations;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Contracts;

// Physical and license items give a quantity; voucher items give an amount.
public sealed record TicketItemRequest(
    [Required] Guid? AssetId,
    [Range(1, Limits.MaxQuantity)] int? Quantity,
    [Range(typeof(decimal), "0.01", Limits.MaxCredit)] decimal? Amount,
    [Range(1, Limits.MaxLoanDays)] int? LoanDays);

// Exactly one of courseId or researchLabId: the requester's own course or lab.
public sealed record CreateTicketRequest(
    Guid? CourseId,
    Guid? ResearchLabId,
    Guid? ProjectId,
    [StringLength(2000)] string? Notes,
    [Required, MinLength(1), MaxLength(Limits.MaxTicketItems)] List<TicketItemRequest> Items);

// Either a state change (status, with an optional comment) or new contents (items, projectId,
// notes), never both. New contents replace the old ones.
public sealed record UpdateTicketRequest(
    TicketStatus? Status,
    [StringLength(2000)] string? Comment,
    Guid? ProjectId,
    [StringLength(2000)] string? Notes,
    [MaxLength(Limits.MaxTicketItems)] List<TicketItemRequest>? Items);

public sealed record TicketQuery
{
    public TicketStatus? Status { get; init; }
    [StringLength(100)] public string? AssetCategory { get; init; }
    public Guid? CourseId { get; init; }
    public Guid? ResearchLabId { get; init; }
    public Guid? ProjectId { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, Limits.MaxPageSize)] public int PageSize { get; init; } = 20;
}

public sealed record TicketResponse(
    Guid Id,
    TicketStatus Status,
    Guid RequesterId,
    Guid? CourseId,
    Guid? ResearchLabId,
    Guid? ProjectId,
    string? Notes,
    IReadOnlyList<TicketItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SignedOffAt,
    Guid? SignedOffById,
    DateTimeOffset? AllocatedAt,
    DateTimeOffset? ClosedAt,
    // Only on GET /tickets/{id}.
    IReadOnlyList<TicketEventResponse>? Events = null,
    TicketTimings? Timings = null);

public sealed record TicketItemResponse(
    Guid AssetId,
    string AssetName,
    AssetType AssetType,
    string Category,
    int? Quantity,
    decimal? Amount,
    int? LoanDays);

public sealed record TicketEventResponse(TicketEventType Type, Guid ActorId, DateTimeOffset At, string? Detail);

// Seconds between lifecycle stages; null until both stages have happened.
public sealed record TicketTimings(
    double? SubmissionToSignOffSeconds,
    double? ApprovalToAllocationSeconds,
    double? AllocationToCloseSeconds,
    int AllocationAttempts,
    int FailedAllocationAttempts);

public sealed record AllocationQuery
{
    public Guid? CourseId { get; init; }
    public Guid? ResearchLabId { get; init; }
}

public sealed record ReturnDeviceRequest([Required, StringLength(64, MinimumLength = 1)] string Token);

public sealed record AllocationResultResponse(
    Guid TicketId,
    TicketStatus Status,
    DateTimeOffset AllocatedAt,
    IReadOnlyList<DeviceAllocationResponse> Devices,
    IReadOnlyList<LicenseKeyAllocationResponse> LicenseKeys,
    IReadOnlyList<VoucherAllocationResponse> Vouchers);

public sealed record DeviceAllocationResponse(
    string Token,
    Guid AssetId,
    string AssetName,
    string Category,
    Guid TicketId,
    DateTimeOffset CheckedOutAt,
    DateTimeOffset DueAt,
    AvailabilityFlag Status,
    double DaysCheckedOut);

public sealed record LicenseKeyAllocationResponse(
    Guid AssetId,
    string AssetName,
    string Key,
    Guid TicketId,
    DateTimeOffset IssuedAt);

public sealed record VoucherAllocationResponse(
    Guid AssetId,
    string AssetName,
    decimal Amount,
    Guid TicketId,
    DateTimeOffset AllocatedAt);

public sealed record AllocationMetrics(
    int DevicesCheckedOut,
    int DevicesOverdue,
    int LicenseKeysAssigned,
    decimal VoucherCreditAllocated);

public sealed record ActiveAllocationsResponse(
    Guid? CourseId,
    Guid? ResearchLabId,
    AllocationMetrics Metrics,
    IReadOnlyList<DeviceAllocationResponse> Devices,
    IReadOnlyList<LicenseKeyAllocationResponse> LicenseKeys,
    IReadOnlyList<VoucherAllocationResponse> Vouchers);

public sealed record ReturnDeviceResponse(
    string Token,
    Guid AssetId,
    Guid TicketId,
    DateTimeOffset CheckedOutAt,
    DateTimeOffset DueAt,
    DateTimeOffset ReturnedAt,
    bool WasOverdue,
    int AvailableUnits);

public sealed record StockShortage(Guid AssetId, string AssetName, decimal Requested, decimal Available);
