using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistPreferredCompanySelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "preferred_company_id",
                table: "user_preferences",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_preferences_preferred_company_id",
                table: "user_preferences",
                column: "preferred_company_id");

            migrationBuilder.AddForeignKey(
                name: "FK_user_preferences_companies_preferred_company_id",
                table: "user_preferences",
                column: "preferred_company_id",
                principalTable: "companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_preferences_companies_preferred_company_id",
                table: "user_preferences");

            migrationBuilder.DropIndex(
                name: "IX_user_preferences_preferred_company_id",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "preferred_company_id",
                table: "user_preferences");
        }
    }
}
