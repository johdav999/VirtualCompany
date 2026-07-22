using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunicationLanguagePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "conversation_language",
                table: "support_cases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "communication_language",
                table: "sales_campaigns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_language",
                table: "contacts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "conversation_language",
                table: "support_cases");

            migrationBuilder.DropColumn(
                name: "communication_language",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "preferred_language",
                table: "contacts");
        }
    }
}
