using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260414130000_AddEscalations")]
    public partial class AddEscalations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string1000Type = isPostgres ? "character varying(1000)" : "nvarchar(1000)";

            migrationBuilder.CreateTable(
                name: "escalations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    policy_id = table.Column<Guid>(type: guidType, nullable: false),
                    source_entity_id = table.Column<Guid>(type: guidType, nullable: false),
                    source_entity_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    escalation_level = table.Column<int>(type: "int", nullable: false),
                    reason = table.Column<string>(type: string1000Type, maxLength: 1000, nullable: false),
                    triggered_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                    lifecycle_version = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false, defaultValue: "triggered"),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalations", x => x.id);
                    table.ForeignKey(
                        name: "FK_escalations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalations_company_id_source_entity_type_source_entity_id",
                table: "escalations",
                columns: new[] { "company_id", "source_entity_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalations_company_id_policy_id_source_entity_id_escalation_level_lifecycle_version",
                table: "escalations",
                columns: new[] { "company_id", "policy_id", "source_entity_id", "escalation_level", "lifecycle_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalations_company_id_correlation_id",
                table: "escalations",
                columns: new[] { "company_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalations_company_id_triggered_at",
                table: "escalations",
                columns: new[] { "company_id", "triggered_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "escalations");
        }
    }
}
