using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCompanyKnowledgeDocuments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceRef = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    StorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    FileExtension = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "metadata_json"),
                    AccessScope = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "access_scope_json"),
                    IngestionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingStartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_documents_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_CompanyId_CreatedUtc",
                table: "knowledge_documents",
                columns: new[] { "CompanyId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_CompanyId_IngestionStatus",
                table: "knowledge_documents",
                columns: new[] { "CompanyId", "IngestionStatus" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_documents");
        }
    }
}
