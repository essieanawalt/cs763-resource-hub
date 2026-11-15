// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Response shapes for allocation, return and active-allocation routes (~100%)
// Human Modifications: None yet
// Verification: AllocationTests, ReturnTests, ActiveAllocationTests
// Confidence: High

using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Contracts;

public sealed record DeviceDto(
    string Token,
    DateTimeOffset CheckedOutAt,
    DateTimeOffset DueAt,
    DateTimeOffset? ReturnedAt,
    string Status);

public sealed record AllocationDto(
    Guid Id,
    Guid TicketId,
    Guid AssetId,
    string AssetName,
    AssetType AssetType,
    string Category,
    DateTimeOffset AllocatedAt,
    int? Quantity,
    decimal? Amount,
    IReadOnlyList<string>? LicenseKeys,
    IReadOnlyList<DeviceDto>? Devices);

public sealed record AllocationResultDto(
    Guid TicketId,
    TicketStatus Status,
    DateTimeOffset AllocatedAt,
    IReadOnlyList<AllocationDto> Allocations);

/// <summary>One requested asset that did not have enough stock or balance.</summary>
public sealed record ShortageDto(Guid AssetId, string AssetName, decimal Requested, decimal Available);

public sealed record ReturnRequest(string? Token);

public sealed record ReturnResultDto(
    string Token,
    Guid AssetId,
    Guid TicketId,
    DateTimeOffset CheckedOutAt,
    DateTimeOffset DueAt,
    DateTimeOffset ReturnedAt,
    double CheckoutDurationHours,
    bool WasOverdue,
    int RemainingStock);

public sealed record AllocationSummaryDto(
    int DevicesCheckedOut,
    int DevicesOverdue,
    int LicenseKeysAssigned,
    decimal VoucherCreditsAllocated);

public sealed record ActiveAllocationsDto(
    Guid? CourseId,
    Guid? ResearchLabId,
    AllocationSummaryDto Summary,
    IReadOnlyList<AllocationDto> Allocations);
