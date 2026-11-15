// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Request and response shapes for /api/v1/assets (~100%)
// Human Modifications: None (review only)
// Verification: AssetCrudTests, AssetQueryTests, SeedDataTests; reviewed by me 2026-11-15
// Confidence: High

using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Contracts;

/// <summary>
/// Body for POST and PUT /api/v1/assets. Which stock fields apply depends on the type:
/// physical uses units and defaultLoanDays, license uses licenseKeys, voucher uses balance.
/// </summary>
public sealed record AssetRequest(
    string? Name,
    AssetType? Type,
    string? Category,
    string? DepartmentProgram,
    int? Units,
    int? DefaultLoanDays,
    List<string>? LicenseKeys,
    decimal? Balance);

/// <summary>Availability by type (spec 6.3). Fields that do not apply to the type are left out.</summary>
public sealed record AvailabilityDto(
    bool IsAvailable,
    decimal? Available = null,
    int? Reserved = null,
    int? Checkedout = null,
    int? Overdue = null,
    int? Issued = null,
    decimal? Balance = null,
    decimal? Allocated = null);

public sealed record AssetDto(
    Guid Id,
    string Name,
    AssetType Type,
    string Category,
    string DepartmentProgram,
    decimal TotalStock,
    decimal RemainingStock,
    int? DefaultLoanDays,
    AvailabilityDto Availability,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
