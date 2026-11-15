// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the seed data from spec section 7 with its fixed ids (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Data;

// Static mock data from spec section 7. Ids are fixed so tests and manual calls can use them.
public static class MockData
{
    public static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static class Ids
    {
        public static readonly Guid CourseCs501 = Guid.Parse("10000000-0000-4000-8000-000000000001");
        public static readonly Guid CourseEe580 = Guid.Parse("10000000-0000-4000-8000-000000000002");

        public static readonly Guid DataScienceLab = Guid.Parse("20000000-0000-4000-8000-000000000001");
        public static readonly Guid EmbeddedSystemsLab = Guid.Parse("20000000-0000-4000-8000-000000000002");

        public static readonly Guid ProjectDashboard = Guid.Parse("30000000-0000-4000-8000-000000000001");
        public static readonly Guid ProjectSmartParking = Guid.Parse("30000000-0000-4000-8000-000000000002");

        public static readonly Guid Chen = Guid.Parse("40000000-0000-4000-8000-000000000001");
        public static readonly Guid Okafor = Guid.Parse("40000000-0000-4000-8000-000000000002");
        public static readonly Guid Ferreira = Guid.Parse("40000000-0000-4000-8000-000000000003");
        public static readonly Guid Raman = Guid.Parse("40000000-0000-4000-8000-000000000004");
        public static readonly Guid Blake = Guid.Parse("40000000-0000-4000-8000-000000000005");
        public static readonly Guid Rivera = Guid.Parse("40000000-0000-4000-8000-000000000006");

        public static readonly Guid RaspberryPi = Guid.Parse("50000000-0000-4000-8000-000000000001");
        public static readonly Guid Laptop = Guid.Parse("50000000-0000-4000-8000-000000000002");
        public static readonly Guid JetBrains = Guid.Parse("50000000-0000-4000-8000-000000000003");
        public static readonly Guid CloudCredits = Guid.Parse("50000000-0000-4000-8000-000000000004");
    }

    public static Course[] Courses =>
    [
        new() { Id = Ids.CourseCs501, Code = "CS501", Section = "A1", Title = "Software Engineering", DepartmentProgram = "Computer Science" },
        new() { Id = Ids.CourseEe580, Code = "EE580", Section = "B1", Title = "Embedded Systems Design", DepartmentProgram = "Electrical Engineering" },
    ];

    public static ResearchLab[] ResearchLabs =>
    [
        new() { Id = Ids.DataScienceLab, Name = "Data Science Lab", DepartmentProgram = "Computer Science" },
        new() { Id = Ids.EmbeddedSystemsLab, Name = "Embedded Systems Lab", DepartmentProgram = "Electrical Engineering" },
    ];

    public static Project[] Projects =>
    [
        new() { Id = Ids.ProjectDashboard, Name = "Course Analytics Dashboard", CourseId = Ids.CourseCs501 },
        new() { Id = Ids.ProjectSmartParking, Name = "Smart Parking Prototype", ResearchLabId = Ids.EmbeddedSystemsLab },
    ];

    public static User[] Users =>
    [
        new() { Id = Ids.Chen, Name = "Dr. Maya Chen", Role = UserRole.CourseInstructor, CourseId = Ids.CourseCs501 },
        new() { Id = Ids.Okafor, Name = "Prof. Daniel Okafor", Role = UserRole.CourseInstructor, CourseId = Ids.CourseEe580 },
        new() { Id = Ids.Ferreira, Name = "Dr. Lucas Ferreira", Role = UserRole.LabAdvisor, ResearchLabId = Ids.DataScienceLab },
        new() { Id = Ids.Raman, Name = "Dr. Priya Raman", Role = UserRole.LabAdvisor, ResearchLabId = Ids.EmbeddedSystemsLab },
        new() { Id = Ids.Blake, Name = "Jordan Blake", Role = UserRole.ItDirector },
        new() { Id = Ids.Rivera, Name = "Sam Rivera", Role = UserRole.Administrator },
    ];

    public static Asset[] Assets =>
    [
        new()
        {
            Id = Ids.RaspberryPi, Name = "Raspberry Pi 5 kit", Type = AssetType.Physical, Category = "IoT node",
            DepartmentProgram = "Electrical Engineering", TotalUnits = 10, AvailableUnits = 10, DefaultLoanDays = 14,
            CreatedAt = SeededAt, UpdatedAt = SeededAt,
        },
        new()
        {
            Id = Ids.Laptop, Name = "Dell Latitude 7450 laptop", Type = AssetType.Physical, Category = "Laptop",
            DepartmentProgram = "Computer Science", TotalUnits = 5, AvailableUnits = 5, DefaultLoanDays = 30,
            CreatedAt = SeededAt, UpdatedAt = SeededAt,
        },
        new()
        {
            Id = Ids.JetBrains, Name = "JetBrains IDE license", Type = AssetType.License, Category = "IDE license",
            DepartmentProgram = "Computer Science", CreatedAt = SeededAt, UpdatedAt = SeededAt,
        },
        new()
        {
            Id = Ids.CloudCredits, Name = "Cloud credit pool", Type = AssetType.Voucher, Category = "Cloud credits",
            DepartmentProgram = "Computer Science", TotalCredit = 5000.00m, Balance = 5000.00m,
            CreatedAt = SeededAt, UpdatedAt = SeededAt,
        },
    ];

    public static LicenseKey[] LicenseKeys =>
    [
        new() { Id = Guid.Parse("60000000-0000-4000-8000-000000000001"), AssetId = Ids.JetBrains, Value = "JBIDE-7K2M-Q9XA" },
        new() { Id = Guid.Parse("60000000-0000-4000-8000-000000000002"), AssetId = Ids.JetBrains, Value = "JBIDE-3PLR-8WVN" },
        new() { Id = Guid.Parse("60000000-0000-4000-8000-000000000003"), AssetId = Ids.JetBrains, Value = "JBIDE-M4TD-2HCE" },
    ];
}
