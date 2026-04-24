using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceAgentInsights : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var provider = migrationBuilder.ActiveProvider ?? string.Empty;
            var isPostgres = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
            var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
            var jsonArrayDefault = isPostgres ? "'[]'::jsonb" : "N'[]'";
            var decimalType = isPostgres ? "numeric(5,4)" : "decimal(5,4)";

            migrationBuilder.CreateTable(
                name: "finance_agent_insights",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    company_id = table.Column<Guid>(nullable: false),
                    check_code = table.Column<string>(maxLength: 128, nullable: false),
                    condition_key = table.Column<string>(maxLength: 256, nullable: false),
                    entity_type = table.Column<string>(maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(maxLength: 128, nullable: false),
                    entity_display_name = table.Column<string>(maxLength: 256, nullable: true),
                    severity = table.Column<string>(maxLength: 32, nullable: false),
                    message = table.Column<string>(maxLength: 4000, nullable: false),
                    recommendation = table.Column<string>(maxLength: 4000, nullable: false),
                    confidence = table.Column<decimal>(type: decimalType, nullable: false),
                    affected_entities_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonArrayDefault),
                    metadata_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: isPostgres ? "'{}'::jsonb" : "N'{}'"),
                    status = table.Column<string>(maxLength: 32, nullable: false),
                    observed_at = table.Column<DateTime>(nullable: false),
                    created_at = table.Column<DateTime>(nullable: false),
                    updated_at = table.Column<DateTime>(nullable: false),
                    resolved_at = table.Column<DateTime>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_agent_insights", x => x.id);
                    table.UniqueConstraint("AK_finance_agent_insights_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_agent_insights_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.CheckConstraint("CK_finance_agent_insights_severity", "severity IN ('low', 'medium', 'high', 'critical')");
                    table.CheckConstraint("CK_finance_agent_insights_status", "status IN ('active', 'resolved')");
                    table.CheckConstraint("CK_finance_agent_insights_confidence", "confidence >= 0 AND confidence <= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_agent_insights_company_id_check_code_status",
                table: "finance_agent_insights",
                columns: new[] { "company_id", "check_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_agent_insights_company_id_check_code_condition_key_entity_type_entity_id",
                table: "finance_agent_insights",
                columns: new[] { "company_id", "check_code", "condition_key", "entity_type", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_agent_insights_company_id_entity_type_entity_id_status",
                table: "finance_agent_insights",
                columns: new[] { "company_id", "entity_type", "entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_agent_insights_company_id_status",
                table: "finance_agent_insights",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_agent_insights_company_id_status_updated_at",
                table: "finance_agent_insights",
                columns: new[] { "company_id", "status", "updated_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_agent_insights");
        }
    }
}