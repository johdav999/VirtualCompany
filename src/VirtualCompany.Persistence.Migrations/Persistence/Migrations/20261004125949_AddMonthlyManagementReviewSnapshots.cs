using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyManagementReviewSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monthly_review_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lens = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    AccessStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AsOfUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SavedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalculationVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_review_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monthly_review_snapshots_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monthly_review_snapshots_CompanyId_CreatedByUserId_Lens_Year_Month_SavedAtUtc",
                table: "monthly_review_snapshots",
                columns: new[] { "CompanyId", "CreatedByUserId", "Lens", "Year", "Month", "SavedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_monthly_review_snapshots_CompanyId_CreatedByUserId_RequestId",
                table: "monthly_review_snapshots",
                columns: new[] { "CompanyId", "CreatedByUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monthly_review_snapshots_CompanyId_PreviousId",
                table: "monthly_review_snapshots",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_review_snapshots_CompanyId_SeriesId_Revision",
                table: "monthly_review_snapshots",
                columns: new[] { "CompanyId", "SeriesId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_review_snapshots");
        }
    }
}
