using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesManagementHistoryAndCapacityProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "sales_activities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NewStageId",
                table: "sales_activities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousStageId",
                table: "sales_activities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordedReason",
                table: "sales_activities",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputsJson",
                table: "revenue_forecast_snapshots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_capacity_proposal_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountableUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    SavedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_capacity_proposal_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_capacity_proposal_revisions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_capacity_proposal_revisions_CompanyId_AccountableUserId_RequestId",
                table: "sales_capacity_proposal_revisions",
                columns: new[] { "CompanyId", "AccountableUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_capacity_proposal_revisions_CompanyId_AccountableUserId_SavedAtUtc",
                table: "sales_capacity_proposal_revisions",
                columns: new[] { "CompanyId", "AccountableUserId", "SavedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_capacity_proposal_revisions_CompanyId_PreviousId",
                table: "sales_capacity_proposal_revisions",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_capacity_proposal_revisions_CompanyId_SeriesId_Revision",
                table: "sales_capacity_proposal_revisions",
                columns: new[] { "CompanyId", "SeriesId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_capacity_proposal_revisions");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "sales_activities");

            migrationBuilder.DropColumn(
                name: "NewStageId",
                table: "sales_activities");

            migrationBuilder.DropColumn(
                name: "PreviousStageId",
                table: "sales_activities");

            migrationBuilder.DropColumn(
                name: "RecordedReason",
                table: "sales_activities");

            migrationBuilder.DropColumn(
                name: "InputsJson",
                table: "revenue_forecast_snapshots");
        }
    }
}
