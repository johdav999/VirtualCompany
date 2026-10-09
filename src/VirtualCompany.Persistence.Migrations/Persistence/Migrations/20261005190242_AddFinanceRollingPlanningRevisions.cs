using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceRollingPlanningRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "revision_id",
                table: "forecasts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "finance_forecast_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NativeVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceAsOfUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_forecast_revisions", x => x.Id);
                    table.UniqueConstraint("AK_finance_forecast_revisions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_finance_forecast_revisions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_forecast_revisions_finance_forecast_revisions_CompanyId_PreviousId",
                        columns: x => new { x.CompanyId, x.PreviousId },
                        principalTable: "finance_forecast_revisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_variance_explanations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonthUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    BudgetVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_variance_explanations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_finance_variance_explanations_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_variance_explanations_finance_accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_forecasts_company_id_revision_id",
                table: "forecasts",
                columns: new[] { "company_id", "revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_forecast_revisions_CompanyId_AuthorId_RequestId",
                table: "finance_forecast_revisions",
                columns: new[] { "CompanyId", "AuthorId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_forecast_revisions_CompanyId_NativeVersion",
                table: "finance_forecast_revisions",
                columns: new[] { "CompanyId", "NativeVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_forecast_revisions_CompanyId_PreviousId",
                table: "finance_forecast_revisions",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_finance_forecast_revisions_CompanyId_SavedUtc",
                table: "finance_forecast_revisions",
                columns: new[] { "CompanyId", "SavedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_variance_explanations_CompanyId_AccountId",
                table: "finance_variance_explanations",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_variance_explanations_CompanyId_AuthorId_RequestId",
                table: "finance_variance_explanations",
                columns: new[] { "CompanyId", "AuthorId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_variance_explanations_CompanyId_MonthUtc",
                table: "finance_variance_explanations",
                columns: new[] { "CompanyId", "MonthUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_forecasts_finance_forecast_revisions_company_id_revision_id",
                table: "forecasts",
                columns: new[] { "company_id", "revision_id" },
                principalTable: "finance_forecast_revisions",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_forecasts_finance_forecast_revisions_company_id_revision_id",
                table: "forecasts");

            migrationBuilder.DropTable(
                name: "finance_forecast_revisions");

            migrationBuilder.DropTable(
                name: "finance_variance_explanations");

            migrationBuilder.DropIndex(
                name: "IX_forecasts_company_id_revision_id",
                table: "forecasts");

            migrationBuilder.DropColumn(
                name: "revision_id",
                table: "forecasts");
        }
    }
}
