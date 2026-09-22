using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRepositoryProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_document_repository_provisionings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    onboarding_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiating_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    directory_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    site_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    drive_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    root_item_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    source_display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    root_display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    enable_writes = table.Column<bool>(type: "bit", nullable: false),
                    writable_folder_item_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    writable_folder_display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    root_permission_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    root_permission_managed = table.Column<bool>(type: "bit", nullable: false),
                    write_permission_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    write_permission_managed = table.Column<bool>(type: "bit", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_provisionings", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_document_repository_provisionings_company_document_repository_onboarding_sessions_onboarding_session_id",
                        column: x => x.onboarding_session_id,
                        principalTable: "company_document_repository_onboarding_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_document_repository_provisioning_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provisioning_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_provisioning_agents", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_document_repository_provisioning_agents_agents_company_id_agent_id",
                        columns: x => new { x.company_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_company_document_repository_provisioning_agents_company_document_repository_provisionings_provisioning_id",
                        column: x => x.provisioning_id,
                        principalTable: "company_document_repository_provisionings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisioning_agents_company_id_agent_id",
                table: "company_document_repository_provisioning_agents",
                columns: new[] { "company_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisioning_agents_company_id_provisioning_id_agent_id",
                table: "company_document_repository_provisioning_agents",
                columns: new[] { "company_id", "provisioning_id", "agent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisioning_agents_provisioning_id",
                table: "company_document_repository_provisioning_agents",
                column: "provisioning_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisionings_company_id_status_next_attempt_at",
                table: "company_document_repository_provisionings",
                columns: new[] { "company_id", "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisionings_connection_id",
                table: "company_document_repository_provisionings",
                column: "connection_id",
                unique: true,
                filter: "[connection_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisionings_onboarding_session_id",
                table: "company_document_repository_provisionings",
                column: "onboarding_session_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_document_repository_provisioning_agents");

            migrationBuilder.DropTable(
                name: "company_document_repository_provisionings");
        }
    }
}
