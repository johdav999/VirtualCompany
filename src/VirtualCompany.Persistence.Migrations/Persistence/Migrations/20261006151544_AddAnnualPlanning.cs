using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "annual_plan_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Timezone = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartMonth = table.Column<int>(type: "int", nullable: false),
                    StartDay = table.Column<int>(type: "int", nullable: false),
                    CalendarVersion = table.Column<long>(type: "bigint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StateRevision = table.Column<int>(type: "int", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_plan_versions", x => x.Id);
                    table.UniqueConstraint("AK_annual_plan_versions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_annual_plan_versions_annual_plan_versions_CompanyId_PreviousId",
                        columns: x => new { x.CompanyId, x.PreviousId },
                        principalTable: "annual_plan_versions",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_annual_plan_versions_approval_requests_CompanyId_ApprovalId",
                        columns: x => new { x.CompanyId, x.ApprovalId },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_annual_plan_versions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "annual_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Quarter = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_annual_allocations_annual_plan_versions_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalTable: "annual_plan_versions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_annual_allocations_budgets_CompanyId_BudgetId",
                        columns: x => new { x.CompanyId, x.BudgetId },
                        principalTable: "budgets",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_annual_allocations_company_goals_CompanyId_GoalId",
                        columns: x => new { x.CompanyId, x.GoalId },
                        principalTable: "company_goals",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "annual_budget_bindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Account = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MonthUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NativeVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_budget_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_annual_budget_bindings_annual_plan_versions_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalTable: "annual_plan_versions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_annual_budget_bindings_budgets_CompanyId_BudgetId",
                        columns: x => new { x.CompanyId, x.BudgetId },
                        principalTable: "budgets",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "annual_objectives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalVersion = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MetricKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BaselineReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Baseline = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Target = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_objectives", x => x.Id);
                    table.UniqueConstraint("AK_annual_objectives_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_annual_objectives_annual_plan_versions_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalTable: "annual_plan_versions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_annual_objectives_company_goals_CompanyId_GoalId",
                        columns: x => new { x.CompanyId, x.GoalId },
                        principalTable: "company_goals",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_annual_objectives_quarterly_reviews_CompanyId_BaselineReviewId",
                        columns: x => new { x.CompanyId, x.BaselineReviewId },
                        principalTable: "quarterly_reviews",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "annual_dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitiativeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_dependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_annual_dependencies_annual_objectives_CompanyId_ObjectiveId",
                        columns: x => new { x.CompanyId, x.ObjectiveId },
                        principalTable: "annual_objectives",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_annual_dependencies_operating_initiatives_CompanyId_InitiativeId",
                        columns: x => new { x.CompanyId, x.InitiativeId },
                        principalTable: "operating_initiatives",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "annual_milestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quarter = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DueUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_milestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_annual_milestones_annual_objectives_CompanyId_ObjectiveId",
                        columns: x => new { x.CompanyId, x.ObjectiveId },
                        principalTable: "annual_objectives",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_annual_allocations_CompanyId_BudgetId",
                table: "annual_allocations",
                columns: new[] { "CompanyId", "BudgetId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_allocations_CompanyId_GoalId",
                table: "annual_allocations",
                columns: new[] { "CompanyId", "GoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_allocations_CompanyId_PlanId",
                table: "annual_allocations",
                columns: new[] { "CompanyId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_budget_bindings_CompanyId_BudgetId",
                table: "annual_budget_bindings",
                columns: new[] { "CompanyId", "BudgetId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_budget_bindings_CompanyId_PlanId",
                table: "annual_budget_bindings",
                columns: new[] { "CompanyId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_budget_bindings_PlanId_BudgetId",
                table: "annual_budget_bindings",
                columns: new[] { "PlanId", "BudgetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_annual_dependencies_CompanyId_InitiativeId",
                table: "annual_dependencies",
                columns: new[] { "CompanyId", "InitiativeId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_dependencies_CompanyId_ObjectiveId",
                table: "annual_dependencies",
                columns: new[] { "CompanyId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_dependencies_ObjectiveId_InitiativeId",
                table: "annual_dependencies",
                columns: new[] { "ObjectiveId", "InitiativeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_annual_milestones_CompanyId_ObjectiveId",
                table: "annual_milestones",
                columns: new[] { "CompanyId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_milestones_ObjectiveId_Quarter",
                table: "annual_milestones",
                columns: new[] { "ObjectiveId", "Quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_annual_objectives_CompanyId_BaselineReviewId",
                table: "annual_objectives",
                columns: new[] { "CompanyId", "BaselineReviewId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_objectives_CompanyId_GoalId",
                table: "annual_objectives",
                columns: new[] { "CompanyId", "GoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_objectives_CompanyId_PlanId",
                table: "annual_objectives",
                columns: new[] { "CompanyId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_objectives_PlanId_GoalId",
                table: "annual_objectives",
                columns: new[] { "PlanId", "GoalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_versions_CompanyId_ApprovalId",
                table: "annual_plan_versions",
                columns: new[] { "CompanyId", "ApprovalId" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_versions_CompanyId_FiscalYear_Version",
                table: "annual_plan_versions",
                columns: new[] { "CompanyId", "FiscalYear", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_versions_CompanyId_PreviousId",
                table: "annual_plan_versions",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_annual_plan_versions_CompanyId_RequestId",
                table: "annual_plan_versions",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annual_allocations");

            migrationBuilder.DropTable(
                name: "annual_budget_bindings");

            migrationBuilder.DropTable(
                name: "annual_dependencies");

            migrationBuilder.DropTable(
                name: "annual_milestones");

            migrationBuilder.DropTable(
                name: "annual_objectives");

            migrationBuilder.DropTable(
                name: "annual_plan_versions");
        }
    }
}
