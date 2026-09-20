using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRepositorySynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "knowledge_document_remote_sources",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastValidatedUtc",
                table: "knowledge_document_remote_sources",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservedRemoteVersion",
                table: "knowledge_document_remote_sources",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnavailableReason",
                table: "knowledge_document_remote_sources",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [knowledge_document_remote_sources] SET [ObservedRemoteVersion] = [RemoteVersion], [IsAvailable] = 1, [LastValidatedUtc] = [LastSeenUtc] WHERE [ObservedRemoteVersion] = '';"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "last_synchronized_at",
                table: "company_document_repository_connections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "synchronization_cursor",
                table: "company_document_repository_connections",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "synchronization_mode",
                table: "company_document_repository_connections",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_document_repository_sync_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReconciliationGeneration = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageCursor = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    PendingDeltaCursor = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ObservedCount = table.Column<int>(type: "int", nullable: false),
                    ChangedCount = table.Column<int>(type: "int", nullable: false),
                    RemovedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextRetryUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_sync_jobs", x => x.Id);
                    table.UniqueConstraint("AK_company_document_repository_sync_jobs_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_company_document_repository_sync_jobs_company_document_repository_connections_CompanyId_ConnectionId",
                        columns: x => new { x.CompanyId, x.ConnectionId },
                        principalTable: "company_document_repository_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_document_repository_tracked_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriveId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ParentItemId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsFolder = table.Column<bool>(type: "bit", nullable: false),
                    RemoteVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    UnavailableReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastSeenGeneration = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_tracked_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_document_repository_tracked_items_company_document_repository_connections_CompanyId_ConnectionId",
                        columns: x => new { x.CompanyId, x.ConnectionId },
                        principalTable: "company_document_repository_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_sync_jobs_CompanyId_ConnectionId_IdempotencyKey",
                table: "company_document_repository_sync_jobs",
                columns: new[] { "CompanyId", "ConnectionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_sync_jobs_Status_NextRetryUtc_CreatedUtc",
                table: "company_document_repository_sync_jobs",
                columns: new[] { "Status", "NextRetryUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_tracked_items_CompanyId_ConnectionId_DriveId_ItemId",
                table: "company_document_repository_tracked_items",
                columns: new[] { "CompanyId", "ConnectionId", "DriveId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_tracked_items_CompanyId_ConnectionId_LastSeenGeneration",
                table: "company_document_repository_tracked_items",
                columns: new[] { "CompanyId", "ConnectionId", "LastSeenGeneration" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_tracked_items_CompanyId_ConnectionId_ParentItemId",
                table: "company_document_repository_tracked_items",
                columns: new[] { "CompanyId", "ConnectionId", "ParentItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_document_repository_sync_jobs");

            migrationBuilder.DropTable(
                name: "company_document_repository_tracked_items");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "knowledge_document_remote_sources");

            migrationBuilder.DropColumn(
                name: "LastValidatedUtc",
                table: "knowledge_document_remote_sources");

            migrationBuilder.DropColumn(
                name: "ObservedRemoteVersion",
                table: "knowledge_document_remote_sources");

            migrationBuilder.DropColumn(
                name: "UnavailableReason",
                table: "knowledge_document_remote_sources");

            migrationBuilder.DropColumn(
                name: "last_synchronized_at",
                table: "company_document_repository_connections");

            migrationBuilder.DropColumn(
                name: "synchronization_cursor",
                table: "company_document_repository_connections");

            migrationBuilder.DropColumn(
                name: "synchronization_mode",
                table: "company_document_repository_connections");
        }
    }
}
