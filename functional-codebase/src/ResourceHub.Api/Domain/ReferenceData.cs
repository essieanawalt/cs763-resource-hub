// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the mock reference entities: courses, labs, projects, users (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

namespace ResourceHub.Api.Domain;

// Data owned by other Project Portal services; here it is static mock data (spec section 7).

public class Course
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Section { get; set; }
    public required string Title { get; set; }
    public required string DepartmentProgram { get; set; }
}

public class ResearchLab
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string DepartmentProgram { get; set; }
}

public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    // Exactly one of these is set.
    public Guid? CourseId { get; set; }
    public Guid? ResearchLabId { get; set; }
}

public class User
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public UserRole Role { get; set; }

    // Set for course instructors and lab advisors only.
    public Guid? CourseId { get; set; }
    public Guid? ResearchLabId { get; set; }
}
