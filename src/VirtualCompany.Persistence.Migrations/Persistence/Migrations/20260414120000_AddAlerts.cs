using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260414120000_AddAlerts")]
    public partial class AddAlerts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
            var string256Type = isPostgres ? "character varying(256)" : "nvarchar(256)";
            var string2000Type = isPostgres ? "character varying(2000)" : "nvarchar(2000)";
            var evidenceDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
            var openFilter = isPostgres ? "\"status\" IN ('open', 'acknowledged')" : "[status] IN (N'open', N'acknowledged')";

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    title = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: string2000Type, maxLength: 2000, nullable: false),
                    evidence_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: evidenceDefault),
                    status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false, defaultValue: "open"),
                    correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    fingerprint = table.Column<string>(type: string256Type, maxLength: 256, nullable: false),
                    source_agent_id = table.Column<Guid>(type: guidType, nullable: true),
                    occurrence_count = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    metadata_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: evidenceDefault),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    last_detected_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    resolved_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    closed_at = table.Column<DateTime>(type: dateTimeType, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_alerts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_alerts_agents_source_agent_id",
                        column: x => x.source_agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(name: "IX_alerts_company_id_created_at", table: "alerts", columns: new[] { "company_id", "created_at" });
            migrationBuilder.CreateIndex(name: "IX_alerts_company_id_type_created_at", table: "alerts", columns: new[] { "company_id", "type", "created_at" });
            migrationBuilder.CreateIndex(name: "IX_alerts_company_id_severity_created_at", table: "alerts", columns: new[] { "company_id", "severity", "created_at" });
            migrationBuilder.CreateIndex(name: "IX_alerts_company_id_status_created_at", table: "alerts", columns: new[] { "company_id", "status", "created_at" });
            migrationBuilder.CreateIndex(name: "IX_alerts_company_id_fingerprint", table: "alerts", columns: new[] { "company_id", "fingerprint" });
            migrationBuilder.CreateIndex(name: "IX_alerts_source_agent_id", table: "alerts", column: "source_agent_id");
            migrationBuilder.CreateIndex(
                name: "IX_alerts_company_id_fingerprint_open",
                table: "alerts",
                columns: new[] { "company_id", "fingerprint" },
                unique: true,
                filter: openFilter);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "alerts");
        }
    }
}
