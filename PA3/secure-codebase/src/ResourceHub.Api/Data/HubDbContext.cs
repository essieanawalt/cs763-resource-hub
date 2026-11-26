// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: EF Core model; PA3 stores license keys as ciphertext, keeps ticket events (restrict + soft delete), adds check-in hashes and the audit table (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18 (migrations applied per test database); AuditTrailTests, LicenseKeyProtectionTests
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Data;

public class HubDbContext : DbContext
{
    public HubDbContext(DbContextOptions<HubDbContext> options)
        : base(options)
    {
    }

    public DbSet<Course> Courses => Set<Course>();
    public DbSet<ResearchLab> ResearchLabs => Set<ResearchLab>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<LicenseKey> LicenseKeys => Set<LicenseKey>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketItem> TicketItems => Set<TicketItem>();
    public DbSet<TicketEvent> TicketEvents => Set<TicketEvent>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<DeviceAssignment> DeviceAssignments => Set<DeviceAssignment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Course>(e =>
        {
            e.ToTable("courses");
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Section).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.DepartmentProgram).HasMaxLength(200);
        });

        model.Entity<ResearchLab>(e =>
        {
            e.ToTable("research_labs");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.DepartmentProgram).HasMaxLength(200);
        });

        model.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ResearchLab>().WithMany().HasForeignKey(x => x.ResearchLabId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ResearchLab>().WithMany().HasForeignKey(x => x.ResearchLabId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Asset>(e =>
        {
            e.ToTable("assets", t =>
            {
                t.HasCheckConstraint("ck_assets_remaining_units_nonnegative", "\"RemainingUnits\" IS NULL OR \"RemainingUnits\" >= 0");
                t.HasCheckConstraint("ck_assets_remaining_within_total", "\"RemainingUnits\" IS NULL OR \"RemainingUnits\" <= \"TotalUnits\"");
                t.HasCheckConstraint("ck_assets_balance_nonnegative", "\"Balance\" IS NULL OR \"Balance\" >= 0");
                t.HasCheckConstraint("ck_assets_balance_within_total", "\"Balance\" IS NULL OR \"Balance\" <= \"TotalBalance\"");
            });
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.DepartmentProgram).HasMaxLength(200);
            e.Property(x => x.TotalBalance).HasPrecision(14, 2);
            e.Property(x => x.Balance).HasPrecision(14, 2);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.DepartmentProgram);
            e.HasIndex(x => x.CreatedAt);
            e.HasMany(x => x.LicenseKeys).WithOne().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<LicenseKey>(e =>
        {
            // Only ciphertext is stored (CWE-312). Uniqueness of key values is checked in the service,
            // because random-nonce ciphertexts of equal keys differ.
            e.ToTable("license_keys", t => t.HasCheckConstraint("ck_license_keys_ciphertext_format", "\"KeyCiphertext\" LIKE 'v1.%'"));
            e.Property(x => x.KeyCiphertext).HasMaxLength(400);
            e.HasIndex(x => x.AssetId);
            e.HasIndex(x => x.IssuedTicketId);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.IssuedTicketId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Ticket>(e =>
        {
            e.ToTable("tickets", t =>
                t.HasCheckConstraint("ck_tickets_course_xor_lab", "(\"CourseId\" IS NULL) <> (\"ResearchLabId\" IS NULL)"));
            // Deleted tickets disappear from every query but keep their row and events (CWE-221).
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CourseId);
            e.HasIndex(x => x.ResearchLabId);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.CreatedAt);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ResearchLab>().WithMany().HasForeignKey(x => x.ResearchLabId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<TicketItem>(e =>
        {
            e.ToTable("ticket_items", t =>
            {
                t.HasCheckConstraint("ck_ticket_items_quantity_positive", "\"Quantity\" IS NULL OR \"Quantity\" > 0");
                t.HasCheckConstraint("ck_ticket_items_amount_positive", "\"Amount\" IS NULL OR \"Amount\" > 0");
            });
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TicketId, x.AssetId }).IsUnique();
        });

        model.Entity<TicketEvent>(e =>
        {
            e.ToTable("ticket_events");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasIndex(x => new { x.TicketId, x.OccurredAt });
        });

        model.Entity<Allocation>(e =>
        {
            e.ToTable("allocations");
            e.Property(x => x.AssetType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.HasOne(x => x.Ticket).WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.LicenseKeys).WithOne().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Devices).WithOne().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<DeviceAssignment>(e =>
        {
            e.ToTable("device_assignments");
            e.Property(x => x.Token).HasMaxLength(40);
            e.Property(x => x.CheckInCodeHash).HasMaxLength(64);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => new { x.AssetId, x.ReturnedAt });
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.Property(x => x.Seq).UseIdentityAlwaysColumn();
            e.Property(x => x.ActorSub).HasMaxLength(100);
            e.Property(x => x.ActorRoles).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.EntityType).HasMaxLength(40);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasIndex(x => x.EntityId);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.ActorSub);
        });

        SeedData.Apply(model);
    }
}
