using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Migration("20260430185000_AddFortnoxWriteCommands")]
public partial class AddFortnoxWriteCommands : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "fortnox_write_commands",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                approval_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                http_method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                target_company = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                payload_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                payload_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                sanitized_payload_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                failure_category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                safe_failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                execution_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                executed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                failed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fortnox_write_commands", x => x.id);
                table.ForeignKey(
                    name: "FK_fortnox_write_commands_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_fortnox_write_commands_finance_integration_connections_company_id_connection_id",
                    columns: x => new { x.company_id, x.connection_id },
                    principalTable: "finance_integration_connections",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_fortnox_write_commands_company_id_approval_id",
            table: "fortnox_write_commands",
            columns: new[] { "company_id", "approval_id" },
            unique: true,
            filter: "approval_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_fortnox_write_commands_company_id_connection_id_created_at",
            table: "fortnox_write_commands",
            columns: new[] { "company_id", "connection_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_fortnox_write_commands_company_id_payload_hash_http_method_path_status",
            table: "fortnox_write_commands",
            columns: new[] { "company_id", "payload_hash", "http_method", "path", "status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "fortnox_write_commands");
    }
}
