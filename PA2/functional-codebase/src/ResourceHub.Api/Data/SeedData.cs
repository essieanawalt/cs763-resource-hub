// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Static mock data from spec section 7, seeded with its fixed ids (~100%)
// Human Modifications: Editor reformatting on save, 2026-11-15 (line breaks only, no behaviour change)
// Verification: SeedDataTests read every seeded asset and user back by id; reviewed by me 2026-11-15
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Data;

public static class SeedData
{
    public static readonly Guid CourseCs501 = Guid.Parse("10000000-0000-4000-8000-000000000001");
    public static readonly Guid CourseEe580 = Guid.Parse("10000000-0000-4000-8000-000000000002");
    public static readonly Guid LabDataScience = Guid.Parse("20000000-0000-4000-8000-000000000001");
    public static readonly Guid LabEmbedded = Guid.Parse("20000000-0000-4000-8000-000000000002");

    private const string ComputerScience = "Computer Science";
    private const string ElectricalEngineering = "Electrical Engineering";

    // Fixed so the migration is stable; staggered so "newest first" has a defined order.
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Apply(ModelBuilder model)
    {
        model.Entity<Course>().HasData(
            new Course { Id = CourseCs501, Code = "CS501", Section = "A1", Title = "Software Engineering", DepartmentProgram = ComputerScience },
            new Course { Id = CourseEe580, Code = "EE580", Section = "B1", Title = "Embedded Systems Design", DepartmentProgram = ElectricalEngineering });

        model.Entity<ResearchLab>().HasData(
            new ResearchLab { Id = LabDataScience, Name = "Data Science Lab", DepartmentProgram = ComputerScience },
            new ResearchLab { Id = LabEmbedded, Name = "Embedded Systems Lab", DepartmentProgram = ElectricalEngineering });

        model.Entity<Project>().HasData(
            new Project { Id = Guid.Parse("30000000-0000-4000-8000-000000000001"), Name = "Course Analytics Dashboard", CourseId = CourseCs501 },
            new Project { Id = Guid.Parse("30000000-0000-4000-8000-000000000002"), Name = "Smart Parking Prototype", ResearchLabId = LabEmbedded });

        model.Entity<User>().HasData(
            new User { Id = Guid.Parse("40000000-0000-4000-8000-000000000001"), Name = "Dr. Maya Chen", Role = UserRole.CourseInstructor, CourseId = CourseCs501 },
            new User { Id = Guid.Parse("40000000-0000-4000-8000-000000000002"), Name = "Prof. Daniel Okafor", Role = UserRole.CourseInstructor, CourseId = CourseEe580 },
            new User { Id = Guid.Parse("40000000-0000-4000-8000-000000000003"), Name = "Dr. Lucas Ferreira", Role = UserRole.LabAdvisor, ResearchLabId = LabDataScience },
            new User { Id = Guid.Parse("40000000-0000-4000-8000-000000000004"), Name = "Dr. Priya Raman", Role = UserRole.LabAdvisor, ResearchLabId = LabEmbedded },
            new User { Id = Guid.Parse("40000000-0000-4000-8000-000000000005"), Name = "Jordan Blake", Role = UserRole.ItDirector },
            new User { Id = Guid.Parse("40000000-0000-4000-8000-000000000006"), Name = "Sam Rivera", Role = UserRole.Administrator });

        var jetBrains = Guid.Parse("50000000-0000-4000-8000-000000000003");
        model.Entity<Asset>().HasData(
            new Asset
            {
                Id = Guid.Parse("50000000-0000-4000-8000-000000000001"),
                Name = "Raspberry Pi 5 kit",
                Type = AssetType.Physical,
                Category = "IoT node",
                DepartmentProgram = ElectricalEngineering,
                TotalUnits = 10,
                RemainingUnits = 10,
                DefaultLoanDays = 14,
                CreatedAt = SeededAt,
                UpdatedAt = SeededAt,
            },
            new Asset
            {
                Id = Guid.Parse("50000000-0000-4000-8000-000000000002"),
                Name = "Dell Latitude 7450 laptop",
                Type = AssetType.Physical,
                Category = "Laptop",
                DepartmentProgram = ComputerScience,
                TotalUnits = 5,
                RemainingUnits = 5,
                DefaultLoanDays = 30,
                CreatedAt = SeededAt.AddMinutes(1),
                UpdatedAt = SeededAt.AddMinutes(1),
            },
            new Asset
            {
                Id = jetBrains,
                Name = "JetBrains IDE license",
                Type = AssetType.License,
                Category = "IDE license",
                DepartmentProgram = ComputerScience,
                TotalUnits = 3,
                RemainingUnits = 3,
                CreatedAt = SeededAt.AddMinutes(2),
                UpdatedAt = SeededAt.AddMinutes(2),
            },
            new Asset
            {
                Id = Guid.Parse("50000000-0000-4000-8000-000000000004"),
                Name = "Cloud credit pool",
                Type = AssetType.Voucher,
                Category = "Cloud credits",
                DepartmentProgram = ComputerScience,
                TotalBalance = 5000.00m,
                Balance = 5000.00m,
                CreatedAt = SeededAt.AddMinutes(3),
                UpdatedAt = SeededAt.AddMinutes(3),
            });

        model.Entity<LicenseKey>().HasData(
            new LicenseKey { Id = Guid.Parse("51000000-0000-4000-8000-000000000001"), AssetId = jetBrains, Key = "JBIDE-7K2M-Q9XA" },
            new LicenseKey { Id = Guid.Parse("51000000-0000-4000-8000-000000000002"), AssetId = jetBrains, Key = "JBIDE-3PLR-8WVN" },
            new LicenseKey { Id = Guid.Parse("51000000-0000-4000-8000-000000000003"), AssetId = jetBrains, Key = "JBIDE-M4TD-2HCE" });
    }
}
