// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Hand-written step 1 of the license key encryption: adds the ciphertext column beside the old plaintext one so startup can encrypt existing rows (~100%)
// Human Modifications: None yet
// Verification: Applied by every test database (dotnet test against PostgreSQL 18) and by docker compose up
// Confidence: High

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceHub.Api.Data.Migrations
{
    /// <summary>
    /// After this step DatabaseStartup encrypts every plaintext key into KeyCiphertext (and inserts
    /// the seeded keys encrypted); 20261126100100_SecurityHardening then drops the plaintext column.
    /// </summary>
    [DbContext(typeof(HubDbContext))]
    [Migration(DatabaseStartup.EncryptLicenseKeysStep1)]
    public partial class EncryptLicenseKeysStep1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_license_keys_AssetId_Key",
                table: "license_keys");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "license_keys",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "KeyCiphertext",
                table: "license_keys",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_AssetId",
                table: "license_keys",
                column: "AssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back would need the plaintext keys, which this version no longer keeps.
            throw new System.NotSupportedException("Encrypted license keys cannot be migrated back to plaintext.");
        }
    }
}
