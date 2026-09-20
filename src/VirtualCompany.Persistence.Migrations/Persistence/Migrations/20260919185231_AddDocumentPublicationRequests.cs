using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    public partial class AddDocumentPublicationRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_document_publication_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_folder_item_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    requesting_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requesting_actor_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    requesting_actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tool_execution_attempt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    policy_decision_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    approval_version_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    provider_item_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    provider_version = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    source_web_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    failure_message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_publication_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_document_publication_requests_company_document_repository_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "company_document_repository_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_publication_requests_company_id_connection_id_status",
                table: "company_document_publication_requests",
                columns: new[] { "company_id", "connection_id", "status" });
            migrationBuilder.CreateIndex(
                name: "IX_company_document_publication_requests_company_id_idempotency_key",
                table: "company_document_publication_requests",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_company_document_publication_requests_company_id_tool_execution_attempt_id",
                table: "company_document_publication_requests",
                columns: new[] { "company_id", "tool_execution_attempt_id" },
                unique: true,
                filter: "[tool_execution_attempt_id] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropTable(name: "company_document_publication_requests");
    }
}