// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Domain entities and enums for assets, tickets, allocations and mock data (~100%)
// Human Modifications: None (review only)
// Verification: API integration tests in ResourceHub.Tests; reviewed by me 2026-11-15
// Confidence: High

namespace ResourceHub.Api.Domain;

/// <summary>Decides how an asset's balance and status work (spec 6.1).</summary>
public enum AssetType
{
    Physical,
    License,
    Voucher,
}

/// <summary>Ticket lifecycle (spec requirement 3).</summary>
public enum TicketStatus
{
    Pending,
    Approved,
    Rejected,
    Allocated,
    Closed,
}

/// <summary>The four roles in spec section 4.</summary>
public enum UserRole
{
    CourseInstructor,
    LabAdvisor,
    ItDirector,
    Administrator,
}

/// <summary>Logged for every ticket state change and every allocation attempt (spec 6.13).</summary>
public enum TicketEventType
{
    Submitted,
    Updated,
    Approved,
    Rejected,
    AllocationSucceeded,
    AllocationFailed,
    Closed,
}

// ---- Static mock data owned by other services (spec section 7) ----

public class Course
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Section { get; set; } = "";
    public string Title { get; set; } = "";
    public string DepartmentProgram { get; set; } = "";
}

public class ResearchLab
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string DepartmentProgram { get; set; } = "";
}

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? CourseId { get; set; }
    public Guid? ResearchLabId { get; set; }
}

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public UserRole Role { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? ResearchLabId { get; set; }
}

// ---- Inventory ----

public class Asset
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public AssetType Type { get; set; }
    public string Category { get; set; } = "";
    public string DepartmentProgram { get; set; } = "";

    /// <summary>Physical: units owned. License: keys held. Null for vouchers.</summary>
    public int? TotalUnits { get; set; }

    /// <summary>Physical: units on the shelf. License: keys not yet issued. Null for vouchers.</summary>
    public int? RemainingUnits { get; set; }

    /// <summary>Physical only.</summary>
    public int? DefaultLoanDays { get; set; }

    /// <summary>Voucher only: total credits in the pool.</summary>
    public decimal? TotalBalance { get; set; }

    /// <summary>Voucher only: credits not yet allocated. Never below zero.</summary>
    public decimal? Balance { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<LicenseKey> LicenseKeys { get; set; } = [];
}

public class LicenseKey
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string Key { get; set; } = "";
    public Guid? IssuedTicketId { get; set; }
    public Guid? AllocationId { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
}

// ---- Help desk tickets ----

public class Ticket
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? ResearchLabId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Notes { get; set; }
    public TicketStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public DateTimeOffset? AllocatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public List<TicketItem> Items { get; set; } = [];
    public List<TicketEvent> Events { get; set; } = [];
}

public class TicketItem
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }

    /// <summary>Physical and license assets.</summary>
    public int? Quantity { get; set; }

    /// <summary>Voucher assets.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Physical only; overrides the asset's default loan period.</summary>
    public int? LoanDays { get; set; }
}

public class TicketEvent
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public TicketEventType Type { get; set; }
    public TicketStatus? FromStatus { get; set; }
    public TicketStatus? ToStatus { get; set; }
    public Guid? ActorId { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

// ---- Allocations ----

public class Allocation
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public AssetType AssetType { get; set; }
    public int? Quantity { get; set; }
    public decimal? Amount { get; set; }
    public DateTimeOffset AllocatedAt { get; set; }

    public List<LicenseKey> LicenseKeys { get; set; } = [];
    public List<DeviceAssignment> Devices { get; set; } = [];
}

/// <summary>One physical device handed out, identified by its hardware assignment token.</summary>
public class DeviceAssignment
{
    public Guid Id { get; set; }
    public string Token { get; set; } = "";
    public Guid AllocationId { get; set; }
    public Guid AssetId { get; set; }
    public Guid TicketId { get; set; }
    public DateTimeOffset CheckedOutAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
}
