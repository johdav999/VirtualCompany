using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddInsightAcknowledgments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "insight_acknowledgments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    insight_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_insight_acknowledgments", x => x.id);
                    table.ForeignKey(
                        name: "FK_insight_acknowledgments_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_insight_acknowledgments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_insight_acknowledgments_company_id_user_id_acknowledged_at",
                table: "insight_acknowledgments",
                columns: new[] { "company_id", "user_id", "acknowledged_at" });

            migrationBuilder.CreateIndex(
                name: "IX_insight_acknowledgments_company_id_user_id_insight_key",
                table: "insight_acknowledgments",
                columns: new[] { "company_id", "user_id", "insight_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_insight_acknowledgments_user_id",
                table: "insight_acknowledgments",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "insight_acknowledgments");
        }
    }
}