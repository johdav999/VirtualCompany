using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategicScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strategic_scenario_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DerivedFromId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AnnualPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ForecastRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CalculationVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceRevenue = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    SourceExpense = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Drivers_Years = table.Column<int>(type: "int", nullable: false),
                    Drivers_CapacityUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Drivers_SourceToAnnualScale = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_DemandUnits = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_CapacityUnits = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_DemandGrowthPercent = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_PriceGrowthPercent = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_CostGrowthPercent = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_CapacityGrowthPercent = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_VariableCostShare = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_OpeningCash = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_OpeningReceivables = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_OpeningPayables = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_CollectionShare = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_PaymentShare = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false),
                    Drivers_CashFloor = table.Column<decimal>(type: "decimal(25,10)", precision: 25, scale: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategic_scenario_versions", x => x.Id);
                    table.UniqueConstraint("AK_strategic_scenario_versions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_strategic_scenario_versions_annual_plan_versions_CompanyId_AnnualPlanId",
                        columns: x => new { x.CompanyId, x.AnnualPlanId },
                        principalTable: "annual_plan_versions",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_strategic_scenario_versions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_strategic_scenario_versions_finance_forecast_revisions_CompanyId_ForecastRevisionId",
                        columns: x => new { x.CompanyId, x.ForecastRevisionId },
                        principalTable: "finance_forecast_revisions",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_strategic_scenario_versions_strategic_scenario_versions_CompanyId_DerivedFromId",
                        columns: x => new { x.CompanyId, x.DerivedFromId },
                        principalTable: "strategic_scenario_versions",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_strategic_scenario_versions_strategic_scenario_versions_CompanyId_PreviousId",
                        columns: x => new { x.CompanyId, x.PreviousId },
                        principalTable: "strategic_scenario_versions",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "strategic_scenario_cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Investment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Funding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategic_scenario_cash", x => x.Id);
                    table.ForeignKey(
                        name: "FK_strategic_scenario_cash_strategic_scenario_versions_CompanyId_ScenarioId",
                        columns: x => new { x.CompanyId, x.ScenarioId },
                        principalTable: "strategic_scenario_versions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "strategic_scenario_checkpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InitiativeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategic_scenario_checkpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_strategic_scenario_checkpoints_operating_initiatives_CompanyId_InitiativeId",
                        columns: x => new { x.CompanyId, x.InitiativeId },
                        principalTable: "operating_initiatives",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_strategic_scenario_checkpoints_strategic_scenario_versions_CompanyId_ScenarioId",
                        columns: x => new { x.CompanyId, x.ScenarioId },
                        principalTable: "strategic_scenario_versions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "strategic_scenario_outputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Result_Year = table.Column<int>(type: "int", nullable: false),
                    Result_Demand = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Result_Capacity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Result_Fulfilled = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Result_CapacityShortfall = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Result_Revenue = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_OperatingCost = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_OpeningCash = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_Collections = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_Payments = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_Investment = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_Funding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_ClosingCash = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_Receivables = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_Payables = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Result_FundingGap = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategic_scenario_outputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_strategic_scenario_outputs_strategic_scenario_versions_CompanyId_ScenarioId",
                        columns: x => new { x.CompanyId, x.ScenarioId },
                        principalTable: "strategic_scenario_versions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_cash_CompanyId_ScenarioId",
                table: "strategic_scenario_cash",
                columns: new[] { "CompanyId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_cash_ScenarioId_Year",
                table: "strategic_scenario_cash",
                columns: new[] { "ScenarioId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_checkpoints_CompanyId_InitiativeId",
                table: "strategic_scenario_checkpoints",
                columns: new[] { "CompanyId", "InitiativeId" });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_checkpoints_CompanyId_ScenarioId",
                table: "strategic_scenario_checkpoints",
                columns: new[] { "CompanyId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_outputs_CompanyId_ScenarioId",
                table: "strategic_scenario_outputs",
                columns: new[] { "CompanyId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_outputs_Result_Year",
                table: "strategic_scenario_outputs",
                column: "Result_Year");

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_versions_CompanyId_AnnualPlanId",
                table: "strategic_scenario_versions",
                columns: new[] { "CompanyId", "AnnualPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_versions_CompanyId_DerivedFromId",
                table: "strategic_scenario_versions",
                columns: new[] { "CompanyId", "DerivedFromId" });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_versions_CompanyId_ForecastRevisionId",
                table: "strategic_scenario_versions",
                columns: new[] { "CompanyId", "ForecastRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_versions_CompanyId_PreviousId",
                table: "strategic_scenario_versions",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_versions_CompanyId_RequestId",
                table: "strategic_scenario_versions",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_strategic_scenario_versions_CompanyId_SeriesId_Revision",
                table: "strategic_scenario_versions",
                columns: new[] { "CompanyId", "SeriesId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strategic_scenario_cash");

            migrationBuilder.DropTable(
                name: "strategic_scenario_checkpoints");

            migrationBuilder.DropTable(
                name: "strategic_scenario_outputs");

            migrationBuilder.DropTable(
                name: "strategic_scenario_versions");
        }
    }
}
