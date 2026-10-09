using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportQualityPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "state_revision",
                table: "support_cases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "from_status",
                table: "support_case_events",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_state_baseline",
                table: "support_case_events",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "merge_target_case_id",
                table: "support_case_events",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "state_sequence",
                table: "support_case_events",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "to_status",
                table: "support_case_events",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "support_capacity_proposal_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    TargetYear = table.Column<int>(type: "int", nullable: false),
                    TargetMonth = table.Column<int>(type: "int", nullable: false),
                    ExpectedArrivals = table.Column<int>(type: "int", nullable: false),
                    BacklogToClear = table.Column<int>(type: "int", nullable: false),
                    HandlingMinutes = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AvailablePeople = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    HoursPerBusinessDay = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UtilizationPercent = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ResponseTargetMinutes = table.Column<int>(type: "int", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_capacity_proposal_revisions", x => x.Id);
                    table.UniqueConstraint("AK_support_capacity_proposal_revisions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_support_capacity_proposal_revisions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_capacity_proposal_revisions_support_capacity_proposal_revisions_CompanyId_PreviousId",
                        columns: x => new { x.CompanyId, x.PreviousId },
                        principalTable: "support_capacity_proposal_revisions",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "support_issue_grouping_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupportCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Group = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_issue_grouping_revisions", x => x.Id);
                    table.UniqueConstraint("AK_support_issue_grouping_revisions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_support_issue_grouping_revisions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_issue_grouping_revisions_support_cases_CompanyId_SupportCaseId",
                        columns: x => new { x.CompanyId, x.SupportCaseId },
                        principalTable: "support_cases",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_support_issue_grouping_revisions_support_issue_grouping_revisions_CompanyId_PreviousId",
                        columns: x => new { x.CompanyId, x.PreviousId },
                        principalTable: "support_issue_grouping_revisions",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_case_events_company_id_merge_target_case_id",
                table: "support_case_events",
                columns: new[] { "company_id", "merge_target_case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_support_capacity_proposal_revisions_CompanyId_OwnerId_RequestId",
                table: "support_capacity_proposal_revisions",
                columns: new[] { "CompanyId", "OwnerId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_capacity_proposal_revisions_CompanyId_OwnerId_SavedUtc",
                table: "support_capacity_proposal_revisions",
                columns: new[] { "CompanyId", "OwnerId", "SavedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_support_capacity_proposal_revisions_CompanyId_PreviousId",
                table: "support_capacity_proposal_revisions",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_support_issue_grouping_revisions_CompanyId_ActorId_RequestId",
                table: "support_issue_grouping_revisions",
                columns: new[] { "CompanyId", "ActorId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_issue_grouping_revisions_CompanyId_PreviousId",
                table: "support_issue_grouping_revisions",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_support_issue_grouping_revisions_CompanyId_SupportCaseId_SavedUtc",
                table: "support_issue_grouping_revisions",
                columns: new[] { "CompanyId", "SupportCaseId", "SavedUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_support_case_events_support_cases_company_id_merge_target_case_id",
                table: "support_case_events",
                columns: new[] { "company_id", "merge_target_case_id" },
                principalTable: "support_cases",
                principalColumns: new[] { "company_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_support_case_events_support_cases_company_id_merge_target_case_id",
                table: "support_case_events");

            migrationBuilder.DropTable(
                name: "support_capacity_proposal_revisions");

            migrationBuilder.DropTable(
                name: "support_issue_grouping_revisions");

            migrationBuilder.DropIndex(
                name: "IX_support_case_events_company_id_merge_target_case_id",
                table: "support_case_events");

            migrationBuilder.DropColumn(
                name: "state_revision",
                table: "support_cases");

            migrationBuilder.DropColumn(
                name: "from_status",
                table: "support_case_events");

            migrationBuilder.DropColumn(
                name: "is_state_baseline",
                table: "support_case_events");

            migrationBuilder.DropColumn(
                name: "merge_target_case_id",
                table: "support_case_events");

            migrationBuilder.DropColumn(
                name: "state_sequence",
                table: "support_case_events");

            migrationBuilder.DropColumn(
                name: "to_status",
                table: "support_case_events");
        }
    }
}
