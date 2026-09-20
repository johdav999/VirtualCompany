using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMicrosoft365DocumentRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_document_repository_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    directory_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    application_client_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    credential_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    drive_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    root_item_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_read_only = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    lifecycle_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    audience = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    last_validation_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    last_validation_summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    last_validated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    disconnected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_connections", x => x.id);
                    table.UniqueConstraint("AK_company_document_repository_connections_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_company_document_repository_connections_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_document_repository_agent_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_agent_grants", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_document_repository_agent_grants_agents_company_id_agent_id",
                        columns: x => new { x.company_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_company_document_repository_agent_grants_company_document_repository_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "company_document_repository_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_agent_grants_company_id_agent_id",
                table: "company_document_repository_agent_grants",
                columns: new[] { "company_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_agent_grants_company_id_connection_id_agent_id",
                table: "company_document_repository_agent_grants",
                columns: new[] { "company_id", "connection_id", "agent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_connections_company_id_lifecycle_state",
                table: "company_document_repository_connections",
                columns: new[] { "company_id", "lifecycle_state" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_connections_company_id_provider_kind_directory_tenant_id_drive_id_root_item_id",
                table: "company_document_repository_connections",
                columns: new[] { "company_id", "provider_kind", "directory_tenant_id", "drive_id", "root_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_document_repository_agent_grants");

            migrationBuilder.DropTable(
                name: "company_document_repository_connections");
        }
    }
}
