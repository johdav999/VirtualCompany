using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowRepeatedRepositoryProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_company_document_repository_provisionings_connection_id",
                table: "company_document_repository_provisionings");

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisionings_connection_id",
                table: "company_document_repository_provisionings",
                column: "connection_id",
                filter: "[connection_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_company_document_repository_provisionings_connection_id",
                table: "company_document_repository_provisionings");

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_provisionings_connection_id",
                table: "company_document_repository_provisionings",
                column: "connection_id",
                unique: true,
                filter: "[connection_id] IS NOT NULL");
        }
    }
}
