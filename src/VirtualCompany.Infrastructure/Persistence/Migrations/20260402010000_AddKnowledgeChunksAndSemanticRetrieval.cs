using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddKnowledgeChunksAndSemanticRetrieval : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "knowledge_documents",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveChunkCount",
                table: "knowledge_documents",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentChunkSetVersion",
                table: "knowledge_documents",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "knowledge_documents",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "knowledge_documents",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModelVersion",
                table: "knowledge_documents",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexingFailureCode",
                table: "knowledge_documents",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexingFailureMessage",
                table: "knowledge_documents",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexingStatus",
                table: "knowledge_documents",
                maxLength: 32,
                nullable: false,
                defaultValue: "not_indexed");

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexedUtc",
                table: "knowledge_documents",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexingFailedUtc",
                table: "knowledge_documents",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexingRequestedUtc",
                table: "knowledge_documents",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexingStartedUtc",
                table: "knowledge_documents",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_CompanyId_IndexingStatus",
                table: "knowledge_documents",
                columns: new[] { "CompanyId", "IndexingStatus" });

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS vector;""");

                migrationBuilder.CreateTable(
                    name: "knowledge_chunks",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                        DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                        ChunkSetVersion = table.Column<int>(type: "integer", nullable: false),
                        ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                        IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                        Content = table.Column<string>(type: "text", nullable: false),
                        Embedding = table.Column<string>(type: "vector", nullable: false),
                        Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'", name: "metadata_json"),
                        SourceReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                        StartOffset = table.Column<int>(type: "integer", nullable: true),
                        EndOffset = table.Column<int>(type: "integer", nullable: true),
                        ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        EmbeddingModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                        EmbeddingModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                        EmbeddingDimensions = table.Column<int>(type: "integer", nullable: false),
                        CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_knowledge_chunks", x => x.Id);
                        table.ForeignKey(
                            name: "FK_knowledge_chunks_companies_CompanyId",
                            column: x => x.CompanyId,
                            principalTable: "companies",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_knowledge_chunks_knowledge_documents_DocumentId",
                            column: x => x.DocumentId,
                            principalTable: "knowledge_documents",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "knowledge_chunks",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(nullable: false),
                        CompanyId = table.Column<Guid>(nullable: false),
                        DocumentId = table.Column<Guid>(nullable: false),
                        ChunkSetVersion = table.Column<int>(nullable: false),
                        ChunkIndex = table.Column<int>(nullable: false),
                        IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                        Content = table.Column<string>(nullable: false),
                        Embedding = table.Column<string>(type: "nvarchar(max)", nullable: false),
                        Metadata = table.Column<string>(nullable: false, defaultValue: "{}", name: "metadata_json"),
                        SourceReference = table.Column<string>(maxLength: 1024, nullable: false),
                        StartOffset = table.Column<int>(nullable: true),
                        EndOffset = table.Column<int>(nullable: true),
                        ContentHash = table.Column<string>(maxLength: 64, nullable: false),
                        EmbeddingModel = table.Column<string>(maxLength: 200, nullable: false),
                        EmbeddingModelVersion = table.Column<string>(maxLength: 100, nullable: true),
                        EmbeddingDimensions = table.Column<int>(nullable: false),
                        CreatedUtc = table.Column<DateTime>(nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_knowledge_chunks", x => x.Id);
                        table.ForeignKey(
                            name: "FK_knowledge_chunks_companies_CompanyId",
                            column: x => x.CompanyId,
                            principalTable: "companies",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_knowledge_chunks_knowledge_documents_DocumentId",
                            column: x => x.DocumentId,
                            principalTable: "knowledge_documents",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_CompanyId_CreatedUtc",
                table: "knowledge_chunks",
                columns: new[] { "CompanyId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_CompanyId_IsActive_DocumentId",
                table: "knowledge_chunks",
                columns: new[] { "CompanyId", "IsActive", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_DocumentId_ChunkSetVersion_ChunkIndex",
                table: "knowledge_chunks",
                columns: new[] { "DocumentId", "ChunkSetVersion", "ChunkIndex" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropIndex(
                name: "IX_knowledge_documents_CompanyId_IndexingStatus",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "ActiveChunkCount",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "CurrentChunkSetVersion",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "EmbeddingModelVersion",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexingFailureCode",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexingFailureMessage",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexingStatus",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexedUtc",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexingFailedUtc",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexingRequestedUtc",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexingStartedUtc",
                table: "knowledge_documents");
        }
    }
}
