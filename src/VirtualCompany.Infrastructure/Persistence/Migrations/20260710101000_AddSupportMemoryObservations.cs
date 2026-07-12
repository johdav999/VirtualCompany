using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddSupportMemoryObservations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_memory_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_resolution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_memory_profile_preference_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    evidence_summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,3)", nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    valid_until_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    policy_version = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    source_event_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_memory_observations", x => x.id);
                    table.ForeignKey(
                        name: "FK_support_memory_observations_contacts_company_id_contact_id",
                        columns: x => new { x.company_id, x.contact_id },
                        principalTable: "contacts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_support_memory_observations_support_case_resolutions_company_id_support_case_resolution_id",
                        columns: x => new { x.company_id, x.support_case_resolution_id },
                        principalTable: "support_case_resolutions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_support_memory_observations_support_cases_company_id_support_case_id",
                        columns: x => new { x.company_id, x.support_case_id },
                        principalTable: "support_cases",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_memory_observations_company_id_contact_id_status",
                table: "support_memory_observations",
                columns: new[] { "company_id", "contact_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_support_memory_observations_company_id_source_event_key_contact_id",
                table: "support_memory_observations",
                columns: new[] { "company_id", "source_event_key", "contact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_memory_observations_company_id_status_updated_at",
                table: "support_memory_observations",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_support_memory_observations_company_id_support_case_id",
                table: "support_memory_observations",
                columns: new[] { "company_id", "support_case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_support_memory_observations_company_id_support_case_resolution_id",
                table: "support_memory_observations",
                columns: new[] { "company_id", "support_case_resolution_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "support_memory_observations");
        }
    }
}
