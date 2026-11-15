// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Generated with dotnet ef migrations add InitialHub from the model Claude wrote (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ResourceHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialHub : Migration
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
                    DepartmentProgram = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalUnits = table.Column<int>(type: "integer", nullable: true),
                    AvailableUnits = table.Column<int>(type: "integer", nullable: true),
                    DefaultLoanDays = table.Column<int>(type: "integer", nullable: true),
                    TotalCredit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                    table.CheckConstraint("ck_assets_balance", "\"Balance\" IS NULL OR (\"Balance\" >= 0 AND \"Balance\" <= \"TotalCredit\")");
                    table.CheckConstraint("ck_assets_loan_days", "\"DefaultLoanDays\" IS NULL OR \"DefaultLoanDays\" > 0");
                    table.CheckConstraint("ck_assets_units", "\"AvailableUnits\" IS NULL OR (\"AvailableUnits\" >= 0 AND \"AvailableUnits\" <= \"TotalUnits\")");
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentProgram = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
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
                    DepartmentProgram = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
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
                    table.CheckConstraint("ck_projects_one_owner", "(\"CourseId\" IS NULL) <> (\"ResearchLabId\" IS NULL)");
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
                    SignedOffAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignedOffById = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AllocatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                    table.CheckConstraint("ck_tickets_one_owner", "(\"CourseId\" IS NULL) <> (\"ResearchLabId\" IS NULL)");
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
                        name: "FK_tickets_users_AllocatedById",
                        column: x => x.AllocatedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tickets_users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tickets_users_SignedOffById",
                        column: x => x.SignedOffById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hardware_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckedOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReturnedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hardware_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hardware_assignments_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_hardware_assignments_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_hardware_assignments_users_ReturnedById",
                        column: x => x.ReturnedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "license_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_license_keys_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_license_keys_tickets_TicketId",
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
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
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
                    table.ForeignKey(
                        name: "FK_ticket_events_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    LoanDays = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_items", x => x.Id);
                    table.CheckConstraint("ck_ticket_items_positive", "(\"Quantity\" IS NULL OR \"Quantity\" > 0) AND (\"Amount\" IS NULL OR \"Amount\" > 0) AND (\"LoanDays\" IS NULL OR \"LoanDays\" > 0)");
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
                name: "voucher_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_allocations", x => x.Id);
                    table.CheckConstraint("ck_voucher_allocations_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_voucher_allocations_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_allocations_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "assets",
                columns: new[] { "Id", "AvailableUnits", "Balance", "Category", "CreatedAt", "DefaultLoanDays", "DepartmentProgram", "Name", "TotalCredit", "TotalUnits", "Type", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("50000000-0000-4000-8000-000000000001"), 10, null, "IoT node", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 14, "Electrical Engineering", "Raspberry Pi 5 kit", null, 10, "Physical", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("50000000-0000-4000-8000-000000000002"), 5, null, "Laptop", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, "Computer Science", "Dell Latitude 7450 laptop", null, 5, "Physical", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("50000000-0000-4000-8000-000000000003"), null, null, "IDE license", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Computer Science", "JetBrains IDE license", null, null, "License", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("50000000-0000-4000-8000-000000000004"), null, 5000.00m, "Cloud credits", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Computer Science", "Cloud credit pool", 5000.00m, null, "Voucher", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
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
                table: "license_keys",
                columns: new[] { "Id", "AssetId", "IssuedAt", "TicketId", "Value" },
                values: new object[,]
                {
                    { new Guid("60000000-0000-4000-8000-000000000001"), new Guid("50000000-0000-4000-8000-000000000003"), null, null, "JBIDE-7K2M-Q9XA" },
                    { new Guid("60000000-0000-4000-8000-000000000002"), new Guid("50000000-0000-4000-8000-000000000003"), null, null, "JBIDE-3PLR-8WVN" },
                    { new Guid("60000000-0000-4000-8000-000000000003"), new Guid("50000000-0000-4000-8000-000000000003"), null, null, "JBIDE-M4TD-2HCE" }
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
                name: "IX_assets_CreatedAt",
                table: "assets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_hardware_assignments_AssetId_ReturnedAt",
                table: "hardware_assignments",
                columns: new[] { "AssetId", "ReturnedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_hardware_assignments_ReturnedById",
                table: "hardware_assignments",
                column: "ReturnedById");

            migrationBuilder.CreateIndex(
                name: "IX_hardware_assignments_TicketId",
                table: "hardware_assignments",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_hardware_assignments_Token",
                table: "hardware_assignments",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_AssetId",
                table: "license_keys",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_TicketId",
                table: "license_keys",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_Value",
                table: "license_keys",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_CourseId",
                table: "projects",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_ResearchLabId",
                table: "projects",
                column: "ResearchLabId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_events_ActorId",
                table: "ticket_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_events_TicketId_At",
                table: "ticket_events",
                columns: new[] { "TicketId", "At" });

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
                name: "IX_tickets_AllocatedById",
                table: "tickets",
                column: "AllocatedById");

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
                name: "IX_tickets_SignedOffById",
                table: "tickets",
                column: "SignedOffById");

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

            migrationBuilder.CreateIndex(
                name: "IX_voucher_allocations_AssetId",
                table: "voucher_allocations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_allocations_TicketId",
                table: "voucher_allocations",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hardware_assignments");

            migrationBuilder.DropTable(
                name: "license_keys");

            migrationBuilder.DropTable(
                name: "ticket_events");

            migrationBuilder.DropTable(
                name: "ticket_items");

            migrationBuilder.DropTable(
                name: "voucher_allocations");

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
