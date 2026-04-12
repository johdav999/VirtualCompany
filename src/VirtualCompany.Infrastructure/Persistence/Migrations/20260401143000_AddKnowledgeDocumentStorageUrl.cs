using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddKnowledgeDocumentStorageUrl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AddColumn<string>(
                    name: "StorageUrl",
                    table: "knowledge_documents",
                    type: "nvarchar(2048)",
                    maxLength: 2048,
                    nullable: true);
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "StorageUrl",
                    table: "knowledge_documents",
                    maxLength: 2048,
                    nullable: true);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageUrl",
                table: "knowledge_documents");
        }
    }
}
