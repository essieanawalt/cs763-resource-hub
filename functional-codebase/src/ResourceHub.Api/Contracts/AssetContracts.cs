// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the asset request, query and response contracts (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System.ComponentModel.DataAnnotations;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Contracts;

public static class Limits
{
    public const int MaxUnits = 100_000;
    public const int MaxLoanDays = 365;
    public const int MaxKeysPerRequest = 1_000;
    public const int MaxKeyLength = 200;
    public const string MaxCredit = "1000000000";
    public const int MaxTicketItems = 20;
    public const int MaxQuantity = 1_000;
    public const int MaxPageSize = 100;
}

// Fields that do not apply to the asset's type must be left out.
public sealed record CreateAssetRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] AssetType? Type,
    [Required, StringLength(100, MinimumLength = 1)] string Category,
    [Required, StringLength(100, MinimumLength = 1)] string DepartmentProgram,
    [Range(0, Limits.MaxUnits)] int? TotalUnits,
    [Range(1, Limits.MaxLoanDays)] int? DefaultLoanDays,
    [MaxLength(Limits.MaxKeysPerRequest)] List<string>? LicenseKeys,
    [Range(typeof(decimal), "0", Limits.MaxCredit)] decimal? TotalCredit);

// The type cannot change. Physical: totalUnits and defaultLoanDays. Voucher: totalCredit.
// License: optionally add keys or remove keys that have not been issued.
public sealed record UpdateAssetRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required, StringLength(100, MinimumLength = 1)] string Category,
    [Required, StringLength(100, MinimumLength = 1)] string DepartmentProgram,
    [Range(0, Limits.MaxUnits)] int? TotalUnits,
    [Range(1, Limits.MaxLoanDays)] int? DefaultLoanDays,
    [Range(typeof(decimal), "0", Limits.MaxCredit)] decimal? TotalCredit,
    [MaxLength(Limits.MaxKeysPerRequest)] List<string>? AddLicenseKeys,
    [MaxLength(Limits.MaxKeysPerRequest)] List<Guid>? RemoveLicenseKeyIds);

public sealed record AssetQuery
{
    [StringLength(100)] public string? Category { get; init; }
    [StringLength(100)] public string? DepartmentProgram { get; init; }
    public AssetType? Type { get; init; }
    public AvailabilityFlag? Availability { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, Limits.MaxPageSize)] public int PageSize { get; init; } = 20;
}

public sealed record AssetResponse(
    Guid Id,
    string Name,
    AssetType Type,
    string Category,
    string DepartmentProgram,
    decimal TotalStock,
    decimal RemainingStock,
    bool IsAvailable,
    int? DefaultLoanDays,
    PhysicalAvailability? Physical,
    LicenseAvailability? License,
    VoucherAvailability? Voucher,
    IReadOnlyList<MaskedLicenseKey>? LicenseKeys,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// Counts by the status flags in requirement 2.
public sealed record PhysicalAvailability(int Available, int Reserved, int Checkedout, int Overdue);

public sealed record LicenseAvailability(int KeysAvailable, int KeysIssued);

public sealed record VoucherAvailability(decimal Balance, decimal Allocated);

// Key values are never returned from the catalog, only the last four characters.
public sealed record MaskedLicenseKey(Guid Id, string MaskedValue, bool Issued);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
