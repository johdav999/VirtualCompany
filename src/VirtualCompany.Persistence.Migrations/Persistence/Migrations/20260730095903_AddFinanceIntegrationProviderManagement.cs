using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceIntegrationProviderManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_integration_provider_configuration_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    changed_fields_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    occurred_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_integration_provider_configuration_audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "finance_integration_provider_configurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    redirect_uri = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    scopes_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    credential_secret_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    credential_secret_version = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    validation_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    validation_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    last_validated_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_integration_provider_configurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_integration_provider_configuration_audits_provider_key_occurred_utc",
                table: "finance_integration_provider_configuration_audits",
                columns: new[] { "provider_key", "occurred_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_integration_provider_configurations_provider_key",
                table: "finance_integration_provider_configurations",
                column: "provider_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_integration_provider_configuration_audits");

            migrationBuilder.DropTable(
                name: "finance_integration_provider_configurations");
        }
    }
}
