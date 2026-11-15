// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the asset and license key entities (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

namespace ResourceHub.Api.Domain;

// One table for all three asset types; the type-specific columns are null for the other types.
public class Asset
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public AssetType Type { get; set; }
    public required string Category { get; set; }
    public required string DepartmentProgram { get; set; }

    // Physical
    public int? TotalUnits { get; set; }
    public int? AvailableUnits { get; set; }
    public int? DefaultLoanDays { get; set; }

    // Voucher
    public decimal? TotalCredit { get; set; }
    public decimal? Balance { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Mapped to PostgreSQL xmin for optimistic concurrency.
    public uint Version { get; set; }

    // License: total stock is the number of keys.
    public List<LicenseKey> LicenseKeys { get; set; } = [];
}

public class LicenseKey
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }

    // High-value secret: never logged, and only returned once issued.
    public required string Value { get; set; }

    public Guid? TicketId { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }

    public bool IsIssued => IssuedAt is not null;

    // Enough to tell keys apart without revealing them.
    public string MaskedValue => Value.Length <= 4 ? "****" : "****" + Value[^4..];
}
