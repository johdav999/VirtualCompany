using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRepositoryImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_document_repository_import_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    discovered_count = table.Column<int>(type: "int", nullable: false),
                    processed_count = table.Column<int>(type: "int", nullable: false),
                    failed_count = table.Column<int>(type: "int", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_import_jobs", x => x.id);
                    table.UniqueConstraint("AK_company_document_repository_import_jobs_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_company_document_repository_import_jobs_company_document_repository_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "company_document_repository_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_document_remote_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriveId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RemoteVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceWebUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ImportState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_document_remote_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_document_remote_sources_company_document_repository_connections_CompanyId_ConnectionId",
                        columns: x => new { x.CompanyId, x.ConnectionId },
                        principalTable: "company_document_repository_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_document_remote_sources_knowledge_documents_CompanyId_DocumentId",
                        columns: x => new { x.CompanyId, x.DocumentId },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "company_document_repository_import_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DriveId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    RemoteVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceWebUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CanRetry = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_import_items", x => x.Id);
                    table.UniqueConstraint("AK_company_document_repository_import_items_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_company_document_repository_import_items_company_document_repository_import_jobs_CompanyId_JobId",
                        columns: x => new { x.CompanyId, x.JobId },
                        principalTable: "company_document_repository_import_jobs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_import_items_CompanyId_JobId_DriveId_ItemId_RemoteVersion",
                table: "company_document_repository_import_items",
                columns: new[] { "CompanyId", "JobId", "DriveId", "ItemId", "RemoteVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_import_items_CompanyId_JobId_Status",
                table: "company_document_repository_import_items",
                columns: new[] { "CompanyId", "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_import_jobs_company_id_connection_id_idempotency_key",
                table: "company_document_repository_import_jobs",
                columns: new[] { "company_id", "connection_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_import_jobs_status_created_at",
                table: "company_document_repository_import_jobs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_document_remote_sources_CompanyId_ConnectionId_DriveId_ItemId",
                table: "knowledge_document_remote_sources",
                columns: new[] { "CompanyId", "ConnectionId", "DriveId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_document_remote_sources_CompanyId_DocumentId",
                table: "knowledge_document_remote_sources",
                columns: new[] { "CompanyId", "DocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_document_repository_import_items");

            migrationBuilder.DropTable(
                name: "knowledge_document_remote_sources");

            migrationBuilder.DropTable(
                name: "company_document_repository_import_jobs");
        }
    }
}
