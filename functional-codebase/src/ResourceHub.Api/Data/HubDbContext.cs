// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Barebones API with a health check, Docker and tests" (PA1 prompt 01)
// AI Contribution: Generated the DbContext (~98%)
// Human Modifications: Removed a comment pointing to the PA1 spec (2026-11-14)
// Verification: dotnet build and dotnet test after the edit
// Confidence: High

using Microsoft.EntityFrameworkCore;

namespace ResourceHub.Api.Data;

public class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
}
