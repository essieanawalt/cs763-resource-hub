// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Initial schema migration (dotnet-ef); PA3 removed the plaintext license key seed rows (now seeded encrypted at startup) (~100%)
// Human Modifications: None yet
// Verification: Applied by every test database (dotnet test against PostgreSQL 18) and by docker compose up
// Confidence: High

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ResourceHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DepartmentProgram = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalUnits = table.Column<int>(type: "integer", nullable: true),
                    RemainingUnits = table.Column<int>(type: "integer", nullable: true),
                    DefaultLoanDays = table.Column<int>(type: "integer", nullable: true),
                    TotalBalance = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                    table.CheckConstraint("ck_assets_balance_nonnegative", "\"Balance\" IS NULL OR \"Balance\" >= 0");
                    table.CheckConstraint("ck_assets_balance_within_total", "\"Balance\" IS NULL OR \"Balance\" <= \"TotalBalance\"");
                    table.CheckConstraint("ck_assets_remaining_units_nonnegative", "\"RemainingUnits\" IS NULL OR \"RemainingUnits\" >= 0");
                    table.CheckConstraint("ck_assets_remaining_within_total", "\"RemainingUnits\" IS NULL OR \"RemainingUnits\" <= \"TotalUnits\"");
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentProgram = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_labs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentProgram = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_labs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResearchLabId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_projects_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_research_labs_ResearchLabId",
                        column: x => x.ResearchLabId,
                        principalTable: "research_labs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResearchLabId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_users_research_labs_ResearchLabId",
                        column: x => x.ResearchLabId,
                        principalTable: "research_labs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResearchLabId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                    table.CheckConstraint("ck_tickets_course_xor_lab", "(\"CourseId\" IS NULL) <> (\"ResearchLabId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_tickets_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tickets_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tickets_research_labs_ResearchLabId",
                        column: x => x.ResearchLabId,
                        principalTable: "research_labs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tickets_users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allocations_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocations_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_events_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    LoanDays = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_items", x => x.Id);
                    table.CheckConstraint("ck_ticket_items_amount_positive", "\"Amount\" IS NULL OR \"Amount\" > 0");
                    table.CheckConstraint("ck_ticket_items_quantity_positive", "\"Quantity\" IS NULL OR \"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_ticket_items_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ticket_items_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckedOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_assignments_allocations_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "allocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_assignments_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "license_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssuedTicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_license_keys_allocations_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "allocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_license_keys_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_license_keys_tickets_IssuedTicketId",
                        column: x => x.IssuedTicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "assets",
                columns: new[] { "Id", "Balance", "Category", "CreatedAt", "DefaultLoanDays", "DepartmentProgram", "Name", "RemainingUnits", "TotalBalance", "TotalUnits", "Type", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("50000000-0000-4000-8000-000000000001"), null, "IoT node", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 14, "Electrical Engineering", "Raspberry Pi 5 kit", 10, null, 10, "Physical", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("50000000-0000-4000-8000-000000000002"), null, "Laptop", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 1, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, "Computer Science", "Dell Latitude 7450 laptop", 5, null, 5, "Physical", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 1, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("50000000-0000-4000-8000-000000000003"), null, "IDE license", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 2, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Computer Science", "JetBrains IDE license", 3, null, 3, "License", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 2, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("50000000-0000-4000-8000-000000000004"), 5000.00m, "Cloud credits", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 3, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Computer Science", "Cloud credit pool", null, 5000.00m, null, "Voucher", new DateTimeOffset(new DateTime(2026, 11, 1, 0, 3, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "courses",
                columns: new[] { "Id", "Code", "DepartmentProgram", "Section", "Title" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000001"), "CS501", "Computer Science", "A1", "Software Engineering" },
                    { new Guid("10000000-0000-4000-8000-000000000002"), "EE580", "Electrical Engineering", "B1", "Embedded Systems Design" }
                });

            migrationBuilder.InsertData(
                table: "research_labs",
                columns: new[] { "Id", "DepartmentProgram", "Name" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-4000-8000-000000000001"), "Computer Science", "Data Science Lab" },
                    { new Guid("20000000-0000-4000-8000-000000000002"), "Electrical Engineering", "Embedded Systems Lab" }
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "CourseId", "Name", "ResearchLabId", "Role" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-4000-8000-000000000005"), null, "Jordan Blake", null, "ItDirector" },
                    { new Guid("40000000-0000-4000-8000-000000000006"), null, "Sam Rivera", null, "Administrator" }
                });

            migrationBuilder.InsertData(
                table: "projects",
                columns: new[] { "Id", "CourseId", "Name", "ResearchLabId" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-4000-8000-000000000001"), new Guid("10000000-0000-4000-8000-000000000001"), "Course Analytics Dashboard", null },
                    { new Guid("30000000-0000-4000-8000-000000000002"), null, "Smart Parking Prototype", new Guid("20000000-0000-4000-8000-000000000002") }
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "CourseId", "Name", "ResearchLabId", "Role" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-4000-8000-000000000001"), new Guid("10000000-0000-4000-8000-000000000001"), "Dr. Maya Chen", null, "CourseInstructor" },
                    { new Guid("40000000-0000-4000-8000-000000000002"), new Guid("10000000-0000-4000-8000-000000000002"), "Prof. Daniel Okafor", null, "CourseInstructor" },
                    { new Guid("40000000-0000-4000-8000-000000000003"), null, "Dr. Lucas Ferreira", new Guid("20000000-0000-4000-8000-000000000001"), "LabAdvisor" },
                    { new Guid("40000000-0000-4000-8000-000000000004"), null, "Dr. Priya Raman", new Guid("20000000-0000-4000-8000-000000000002"), "LabAdvisor" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_allocations_AssetId",
                table: "allocations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_allocations_TicketId",
                table: "allocations",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_Category",
                table: "assets",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_assets_CreatedAt",
                table: "assets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_assets_DepartmentProgram",
                table: "assets",
                column: "DepartmentProgram");

            migrationBuilder.CreateIndex(
                name: "IX_device_assignments_AllocationId",
                table: "device_assignments",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_device_assignments_AssetId_ReturnedAt",
                table: "device_assignments",
                columns: new[] { "AssetId", "ReturnedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_assignments_Token",
                table: "device_assignments",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_AllocationId",
                table: "license_keys",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_AssetId_Key",
                table: "license_keys",
                columns: new[] { "AssetId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_IssuedTicketId",
                table: "license_keys",
                column: "IssuedTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_CourseId",
                table: "projects",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_ResearchLabId",
                table: "projects",
                column: "ResearchLabId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_events_TicketId_OccurredAt",
                table: "ticket_events",
                columns: new[] { "TicketId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_AssetId",
                table: "ticket_items",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_items_TicketId_AssetId",
                table: "ticket_items",
                columns: new[] { "TicketId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_CourseId",
                table: "tickets",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_CreatedAt",
                table: "tickets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ProjectId",
                table: "tickets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_RequesterId",
                table: "tickets",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ResearchLabId",
                table: "tickets",
                column: "ResearchLabId");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_Status",
                table: "tickets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_users_CourseId",
                table: "users",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_users_ResearchLabId",
                table: "users",
                column: "ResearchLabId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_assignments");

            migrationBuilder.DropTable(
                name: "license_keys");

            migrationBuilder.DropTable(
                name: "ticket_events");

            migrationBuilder.DropTable(
                name: "ticket_items");

            migrationBuilder.DropTable(
                name: "allocations");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "courses");

            migrationBuilder.DropTable(
                name: "research_labs");
        }
    }
}
