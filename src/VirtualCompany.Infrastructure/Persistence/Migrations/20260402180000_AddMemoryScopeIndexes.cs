using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddMemoryScopeIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_memory_items_CompanyId_AgentId_CreatedUtc",
                table: "memory_items",
                columns: new[] { "CompanyId", "AgentId", "CreatedUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_memory_items_CompanyId_AgentId_CreatedUtc",
                table: "memory_items");
        }
    }
}
