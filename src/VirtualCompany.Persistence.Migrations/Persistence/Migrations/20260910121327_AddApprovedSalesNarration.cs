using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedSalesNarration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_narration_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CacheKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AudioHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TranscriptHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MediaFormat = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMilliseconds = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    PreviewCount = table.Column<long>(type: "bigint", nullable: false),
                    ReusedMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_narration_assets", x => x.Id);
                    table.UniqueConstraint("AK_sales_narration_assets_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_narration_assets_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_narration_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeckVersion = table.Column<int>(type: "int", nullable: false),
                    ProcessingVersion = table.Column<int>(type: "int", nullable: false),
                    AudienceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AudienceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ManifestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Voice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConfigurationVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetainUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_narration_revisions", x => x.Id);
                    table.UniqueConstraint("AK_sales_narration_revisions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_narration_revisions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_narration_revisions_sales_meeting_sessions_CompanyId_SessionId",
                        columns: x => new { x.CompanyId, x.SessionId },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_narration_revisions_sales_presentation_decks_CompanyId_DeckId",
                        columns: x => new { x.CompanyId, x.DeckId },
                        principalTable: "sales_presentation_decks",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_narration_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReservedTokens = table.Column<int>(type: "int", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    GeneratedMilliseconds = table.Column<int>(type: "int", nullable: false),
                    ProviderResponseId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UsageJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    RateVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_narration_attempts", x => x.Id);
                    table.UniqueConstraint("AK_sales_narration_attempts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_narration_attempts_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_narration_attempts_sales_narration_assets_CompanyId_AssetId",
                        columns: x => new { x.CompanyId, x.AssetId },
                        principalTable: "sales_narration_assets",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_narration_attempts_sales_narration_revisions_CompanyId_ApprovalRevisionId",
                        columns: x => new { x.CompanyId, x.ApprovalRevisionId },
                        principalTable: "sales_narration_revisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_narration_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSlideId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlideNumber = table.Column<int>(type: "int", nullable: false),
                    TalkingPoint = table.Column<int>(type: "int", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceText = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    Script = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    ScriptHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reused = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_narration_segments", x => x.Id);
                    table.UniqueConstraint("AK_sales_narration_segments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_narration_segments_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_narration_segments_sales_narration_assets_CompanyId_AssetId",
                        columns: x => new { x.CompanyId, x.AssetId },
                        principalTable: "sales_narration_assets",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_narration_segments_sales_narration_revisions_CompanyId_RevisionId",
                        columns: x => new { x.CompanyId, x.RevisionId },
                        principalTable: "sales_narration_revisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_assets_CompanyId_CacheKey",
                table: "sales_narration_assets",
                columns: new[] { "CompanyId", "CacheKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_assets_Status_LeaseUntilUtc",
                table: "sales_narration_assets",
                columns: new[] { "Status", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_attempts_CompanyId_ApprovalRevisionId",
                table: "sales_narration_attempts",
                columns: new[] { "CompanyId", "ApprovalRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_attempts_CompanyId_AssetId_Number",
                table: "sales_narration_attempts",
                columns: new[] { "CompanyId", "AssetId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_attempts_CompanyId_StartedUtc",
                table: "sales_narration_attempts",
                columns: new[] { "CompanyId", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_revisions_CompanyId_DeckId",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "DeckId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_revisions_CompanyId_SessionId_ManifestHash",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "SessionId", "ManifestHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_segments_CompanyId_AssetId",
                table: "sales_narration_segments",
                columns: new[] { "CompanyId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_segments_CompanyId_RevisionId_SlideNumber_TalkingPoint",
                table: "sales_narration_segments",
                columns: new[] { "CompanyId", "RevisionId", "SlideNumber", "TalkingPoint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_narration_attempts");

            migrationBuilder.DropTable(
                name: "sales_narration_segments");

            migrationBuilder.DropTable(
                name: "sales_narration_assets");

            migrationBuilder.DropTable(
                name: "sales_narration_revisions");
        }
    }
}
