// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the domain enums (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

namespace ResourceHub.Api.Domain;

// JSON uses snake_case names (see Program.cs), e.g. "course_instructor", "checkedout".

public enum UserRole
{
    CourseInstructor,
    LabAdvisor,
    ItDirector,
    Administrator,
}

// Decides how an asset's balance and status work (spec 6.1).
public enum AssetType
{
    Physical,
    License,
    Voucher,
}

// Status flags from requirement 2. "Checkedout" is spelled as in the spec.
public enum AvailabilityFlag
{
    Available,
    Reserved,
    Checkedout,
    Overdue,
}

public enum TicketStatus
{
    Pending,
    Approved,
    Rejected,
    Allocated,
    Closed,
}

public enum TicketEventType
{
    Submitted,
    Updated,
    Approved,
    Rejected,
    AllocationSucceeded,
    AllocationFailed,
    DeviceReturned,
    Closed,
}
