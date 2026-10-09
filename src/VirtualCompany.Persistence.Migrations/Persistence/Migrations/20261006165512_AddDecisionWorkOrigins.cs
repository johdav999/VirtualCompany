using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionWorkOrigins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decision_work_origins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceVersion = table.Column<int>(type: "int", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MonthlySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuarterReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnnualPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScenarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Objective = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AcceptanceOutcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ProposedConstraints = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PreviewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviewChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_work_origins", x => x.Id);
                    table.UniqueConstraint("AK_decision_work_origins_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_decision_work_source", "([SourceKind] = 'month' AND [MonthlySnapshotId] = [SourceVersionId] AND [MonthlySnapshotId] IS NOT NULL AND [QuarterReviewId] IS NULL AND [AnnualPlanId] IS NULL AND [ScenarioId] IS NULL) OR ([SourceKind] = 'quarter' AND [QuarterReviewId] = [SourceVersionId] AND [QuarterReviewId] IS NOT NULL AND [MonthlySnapshotId] IS NULL AND [AnnualPlanId] IS NULL AND [ScenarioId] IS NULL) OR ([SourceKind] = 'annual' AND [AnnualPlanId] = [SourceVersionId] AND [AnnualPlanId] IS NOT NULL AND [MonthlySnapshotId] IS NULL AND [QuarterReviewId] IS NULL AND [ScenarioId] IS NULL) OR ([SourceKind] = 'scenario' AND [ScenarioId] = [SourceVersionId] AND [ScenarioId] IS NOT NULL AND [MonthlySnapshotId] IS NULL AND [QuarterReviewId] IS NULL AND [AnnualPlanId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_decision_work_origins_annual_plan_versions_CompanyId_AnnualPlanId",
                        columns: x => new { x.CompanyId, x.AnnualPlanId },
                        principalTable: "annual_plan_versions",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_decision_work_origins_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_decision_work_origins_monthly_review_snapshots_CompanyId_MonthlySnapshotId",
                        columns: x => new { x.CompanyId, x.MonthlySnapshotId },
                        principalTable: "monthly_review_snapshots",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_decision_work_origins_quarterly_reviews_CompanyId_QuarterReviewId",
                        columns: x => new { x.CompanyId, x.QuarterReviewId },
                        principalTable: "quarterly_reviews",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_decision_work_origins_strategic_scenario_versions_CompanyId_ScenarioId",
                        columns: x => new { x.CompanyId, x.ScenarioId },
                        principalTable: "strategic_scenario_versions",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_decision_work_origins_tasks_CompanyId_TaskId",
                        columns: x => new { x.CompanyId, x.TaskId },
                        principalTable: "tasks",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_decision_work_origins_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "decision_work_collaborators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_work_collaborators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_decision_work_collaborators_decision_work_origins_CompanyId_OriginId",
                        columns: x => new { x.CompanyId, x.OriginId },
                        principalTable: "decision_work_origins",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_decision_work_collaborators_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_collaborators_CompanyId_OriginId",
                table: "decision_work_collaborators",
                columns: new[] { "CompanyId", "OriginId" });

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_collaborators_OriginId_UserId",
                table: "decision_work_collaborators",
                columns: new[] { "OriginId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_collaborators_UserId",
                table: "decision_work_collaborators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_CompanyId_AnnualPlanId",
                table: "decision_work_origins",
                columns: new[] { "CompanyId", "AnnualPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_CompanyId_MonthlySnapshotId",
                table: "decision_work_origins",
                columns: new[] { "CompanyId", "MonthlySnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_CompanyId_QuarterReviewId",
                table: "decision_work_origins",
                columns: new[] { "CompanyId", "QuarterReviewId" });

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_CompanyId_RequestId",
                table: "decision_work_origins",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_CompanyId_ScenarioId",
                table: "decision_work_origins",
                columns: new[] { "CompanyId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_CompanyId_SourceKind_SourceVersionId_ItemKey",
                table: "decision_work_origins",
                columns: new[] { "CompanyId", "SourceKind", "SourceVersionId", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_CompanyId_TaskId",
                table: "decision_work_origins",
                columns: new[] { "CompanyId", "TaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_decision_work_origins_OwnerUserId",
                table: "decision_work_origins",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decision_work_collaborators");

            migrationBuilder.DropTable(
                name: "decision_work_origins");
        }
    }
}
