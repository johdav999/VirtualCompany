using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddKnowledgeDocumentChunkSetFingerprint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentChunkSetFingerprint",
                table: "knowledge_documents",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_CompanyId_DocumentId_ChunkSetVersion_IsActive",
                table: "knowledge_chunks",
                columns: new[] { "CompanyId", "DocumentId", "ChunkSetVersion", "IsActive" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_knowledge_chunks_CompanyId_DocumentId_ChunkSetVersion_IsActive",
                table: "knowledge_chunks");

            migrationBuilder.DropColumn(
                name: "CurrentChunkSetFingerprint",
                table: "knowledge_documents");
        }
    }
}
