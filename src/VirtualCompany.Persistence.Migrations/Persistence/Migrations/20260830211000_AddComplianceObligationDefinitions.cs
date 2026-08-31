using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260830211000_AddComplianceObligationDefinitions")]
    public sealed class AddComplianceObligationDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compliance_obligation_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyPackKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PolicyPackVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyPackDefinitionHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DueDateRule = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RequiredReport = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    RequiredEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    SubmissionMode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_obligation_definitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_compliance_obligation_definitions_CompanyId_Key_PolicyPackKey_PolicyPackVersion",
                table: "compliance_obligation_definitions",
                columns: new[] { "CompanyId", "Key", "PolicyPackKey", "PolicyPackVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compliance_obligation_definitions");
        }
    }
}
