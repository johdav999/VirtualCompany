using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowRepeatedPresetSourceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_decks_company_id_session_id_content_hash_processing_version",
                table: "sales_presentation_decks");

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_session_id_content_hash_processing_version",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "session_id", "content_hash", "processing_version" },
                unique: true,
                filter: "[presentation_run_id] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_decks_company_id_session_id_content_hash_processing_version",
                table: "sales_presentation_decks");

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_session_id_content_hash_processing_version",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "session_id", "content_hash", "processing_version" },
                unique: true);
        }
    }
}
