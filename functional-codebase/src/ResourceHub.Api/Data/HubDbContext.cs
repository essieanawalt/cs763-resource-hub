// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Generated the DbContext (PA1), then added the PA2 entities, constraints and seed data (~98%)
// Human Modifications: Removed a comment pointing to the PA1 spec (2026-11-14)
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Data;

public class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<ResearchLab> ResearchLabs => Set<ResearchLab>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<LicenseKey> LicenseKeys => Set<LicenseKey>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketItem> TicketItems => Set<TicketItem>();
    public DbSet<TicketEvent> TicketEvents => Set<TicketEvent>();
    public DbSet<HardwareAssignment> HardwareAssignments => Set<HardwareAssignment>();
    public DbSet<VoucherAllocation> VoucherAllocations => Set<VoucherAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(b =>
        {
            b.ToTable("courses");
            b.Property(c => c.Code).HasMaxLength(20);
            b.Property(c => c.Section).HasMaxLength(20);
            b.Property(c => c.Title).HasMaxLength(200);
            b.Property(c => c.DepartmentProgram).HasMaxLength(100);
            b.HasData(MockData.Courses);
        });

        modelBuilder.Entity<ResearchLab>(b =>
        {
            b.ToTable("research_labs");
            b.Property(l => l.Name).HasMaxLength(200);
            b.Property(l => l.DepartmentProgram).HasMaxLength(100);
            b.HasData(MockData.ResearchLabs);
        });

        modelBuilder.Entity<Project>(b =>
        {
            b.ToTable("projects", t => t.HasCheckConstraint(
                "ck_projects_one_owner", "(\"CourseId\" IS NULL) <> (\"ResearchLabId\" IS NULL)"));
            b.Property(p => p.Name).HasMaxLength(200);
            b.HasOne<Course>().WithMany().HasForeignKey(p => p.CourseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ResearchLab>().WithMany().HasForeignKey(p => p.ResearchLabId).OnDelete(DeleteBehavior.Restrict);
            b.HasData(MockData.Projects);
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.Name).HasMaxLength(200);
            b.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
            b.HasOne<Course>().WithMany().HasForeignKey(u => u.CourseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ResearchLab>().WithMany().HasForeignKey(u => u.ResearchLabId).OnDelete(DeleteBehavior.Restrict);
            b.HasData(MockData.Users);
        });

        modelBuilder.Entity<Asset>(b =>
        {
            b.ToTable("assets", t =>
            {
                // Backstops for the checks made in code: stock never below zero or above the total.
                t.HasCheckConstraint("ck_assets_units",
                    "\"AvailableUnits\" IS NULL OR (\"AvailableUnits\" >= 0 AND \"AvailableUnits\" <= \"TotalUnits\")");
                t.HasCheckConstraint("ck_assets_balance",
                    "\"Balance\" IS NULL OR (\"Balance\" >= 0 AND \"Balance\" <= \"TotalCredit\")");
                t.HasCheckConstraint("ck_assets_loan_days",
                    "\"DefaultLoanDays\" IS NULL OR \"DefaultLoanDays\" > 0");
            });
            b.Property(a => a.Name).HasMaxLength(200);
            b.Property(a => a.Category).HasMaxLength(100);
            b.Property(a => a.DepartmentProgram).HasMaxLength(100);
            b.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.TotalCredit).HasPrecision(12, 2);
            b.Property(a => a.Balance).HasPrecision(12, 2);
            b.Property(a => a.Version).IsRowVersion();
            b.HasIndex(a => a.CreatedAt);
            b.HasMany(a => a.LicenseKeys).WithOne().HasForeignKey(k => k.AssetId).OnDelete(DeleteBehavior.Cascade);
            b.HasData(MockData.Assets);
        });

        modelBuilder.Entity<LicenseKey>(b =>
        {
            b.ToTable("license_keys");
            b.Property(k => k.Value).HasMaxLength(200);
            b.HasIndex(k => k.Value).IsUnique();
            b.HasIndex(k => k.TicketId);
            b.HasOne<Ticket>().WithMany().HasForeignKey(k => k.TicketId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(k => k.IsIssued);
            b.Ignore(k => k.MaskedValue);
            b.HasData(MockData.LicenseKeys);
        });

        modelBuilder.Entity<Ticket>(b =>
        {
            b.ToTable("tickets", t => t.HasCheckConstraint(
                "ck_tickets_one_owner", "(\"CourseId\" IS NULL) <> (\"ResearchLabId\" IS NULL)"));
            b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(t => t.Notes).HasMaxLength(2000);
            b.Property(t => t.Version).IsRowVersion();
            b.HasIndex(t => t.CreatedAt);
            b.HasIndex(t => t.Status);
            b.HasIndex(t => t.CourseId);
            b.HasIndex(t => t.ResearchLabId);
            b.HasIndex(t => t.ProjectId);
            b.HasOne<User>().WithMany().HasForeignKey(t => t.RequesterId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<User>().WithMany().HasForeignKey(t => t.SignedOffById).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<User>().WithMany().HasForeignKey(t => t.AllocatedById).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Course>().WithMany().HasForeignKey(t => t.CourseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ResearchLab>().WithMany().HasForeignKey(t => t.ResearchLabId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(t => t.Items).WithOne().HasForeignKey(i => i.TicketId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(t => t.Events).WithOne().HasForeignKey(e => e.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketItem>(b =>
        {
            b.ToTable("ticket_items", t => t.HasCheckConstraint("ck_ticket_items_positive",
                "(\"Quantity\" IS NULL OR \"Quantity\" > 0) AND (\"Amount\" IS NULL OR \"Amount\" > 0) " +
                "AND (\"LoanDays\" IS NULL OR \"LoanDays\" > 0)"));
            b.Property(i => i.Amount).HasPrecision(12, 2);
            b.HasIndex(i => new { i.TicketId, i.AssetId }).IsUnique();
            b.HasOne(i => i.Asset).WithMany().HasForeignKey(i => i.AssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TicketEvent>(b =>
        {
            b.ToTable("ticket_events");
            b.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(e => e.Detail).HasMaxLength(2000);
            b.HasIndex(e => new { e.TicketId, e.At });
            b.HasOne<User>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HardwareAssignment>(b =>
        {
            b.ToTable("hardware_assignments");
            b.Property(h => h.Token).HasMaxLength(64);
            b.HasIndex(h => h.Token).IsUnique();
            b.HasIndex(h => new { h.AssetId, h.ReturnedAt });
            b.HasIndex(h => h.TicketId);
            b.HasOne(h => h.Asset).WithMany().HasForeignKey(h => h.AssetId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Ticket>().WithMany().HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<User>().WithMany().HasForeignKey(h => h.ReturnedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VoucherAllocation>(b =>
        {
            b.ToTable("voucher_allocations", t => t.HasCheckConstraint(
                "ck_voucher_allocations_positive", "\"Amount\" > 0"));
            b.Property(v => v.Amount).HasPrecision(12, 2);
            b.HasIndex(v => v.TicketId);
            b.HasOne(v => v.Asset).WithMany().HasForeignKey(v => v.AssetId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Ticket>().WithMany().HasForeignKey(v => v.TicketId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
