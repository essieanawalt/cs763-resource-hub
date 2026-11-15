// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Lets dotnet-ef create migrations without a running database or secrets (~100%)
// Human Modifications: None yet
// Verification: dotnet-ef migrations add ran with this factory
// Confidence: High

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ResourceHub.Api.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args)
    {
        // Only used to generate migrations; no connection is opened.
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only")
            .Options;
        return new HubDbContext(options);
    }
}
