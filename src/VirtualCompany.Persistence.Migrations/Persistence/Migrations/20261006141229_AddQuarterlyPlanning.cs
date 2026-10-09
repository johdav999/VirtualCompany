using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuarterlyPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_operating_initiatives_company_id_id",
                table: "operating_initiatives",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_monthly_review_snapshots_CompanyId_Id",
                table: "monthly_review_snapshots",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_company_goals_company_id_id",
                table: "company_goals",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "quarterly_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Quarter = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Timezone = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CalendarVersion = table.Column<long>(type: "bigint", nullable: false),
                    StartMonth = table.Column<int>(type: "int", nullable: false),
                    StartDay = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarterly_reviews", x => x.Id);
                    table.UniqueConstraint("AK_quarterly_reviews_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_quarterly_reviews_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_quarterly_reviews_quarterly_reviews_CompanyId_PreviousId",
                        columns: x => new { x.CompanyId, x.PreviousId },
                        principalTable: "quarterly_reviews",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "quarterly_objectives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalVersion = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Baseline = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Target = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarterly_objectives", x => x.Id);
                    table.UniqueConstraint("AK_quarterly_objectives_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_quarterly_objectives_company_goals_CompanyId_GoalId",
                        columns: x => new { x.CompanyId, x.GoalId },
                        principalTable: "company_goals",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_quarterly_objectives_quarterly_reviews_CompanyId_ReviewId",
                        columns: x => new { x.CompanyId, x.ReviewId },
                        principalTable: "quarterly_reviews",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quarterly_resource_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pool = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AvailableHours = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ProposedHours = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarterly_resource_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quarterly_resource_allocations_company_goals_CompanyId_GoalId",
                        columns: x => new { x.CompanyId, x.GoalId },
                        principalTable: "company_goals",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_quarterly_resource_allocations_monthly_review_snapshots_CompanyId_SnapshotId",
                        columns: x => new { x.CompanyId, x.SnapshotId },
                        principalTable: "monthly_review_snapshots",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_quarterly_resource_allocations_quarterly_reviews_CompanyId_ReviewId",
                        columns: x => new { x.CompanyId, x.ReviewId },
                        principalTable: "quarterly_reviews",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quarterly_initiative_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitiativeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarterly_initiative_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quarterly_initiative_links_operating_initiatives_CompanyId_InitiativeId",
                        columns: x => new { x.CompanyId, x.InitiativeId },
                        principalTable: "operating_initiatives",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_quarterly_initiative_links_quarterly_objectives_CompanyId_ObjectiveId",
                        columns: x => new { x.CompanyId, x.ObjectiveId },
                        principalTable: "quarterly_objectives",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quarterly_measure_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasureKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SnapshotChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarterly_measure_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quarterly_measure_links_monthly_review_snapshots_CompanyId_SnapshotId",
                        columns: x => new { x.CompanyId, x.SnapshotId },
                        principalTable: "monthly_review_snapshots",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_quarterly_measure_links_quarterly_objectives_CompanyId_ObjectiveId",
                        columns: x => new { x.CompanyId, x.ObjectiveId },
                        principalTable: "quarterly_objectives",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quarterly_milestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarterly_milestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quarterly_milestones_quarterly_objectives_CompanyId_ObjectiveId",
                        columns: x => new { x.CompanyId, x.ObjectiveId },
                        principalTable: "quarterly_objectives",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_initiative_links_CompanyId_InitiativeId",
                table: "quarterly_initiative_links",
                columns: new[] { "CompanyId", "InitiativeId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_initiative_links_CompanyId_ObjectiveId",
                table: "quarterly_initiative_links",
                columns: new[] { "CompanyId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_initiative_links_ObjectiveId_InitiativeId",
                table: "quarterly_initiative_links",
                columns: new[] { "ObjectiveId", "InitiativeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_measure_links_CompanyId_ObjectiveId",
                table: "quarterly_measure_links",
                columns: new[] { "CompanyId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_measure_links_CompanyId_SnapshotId",
                table: "quarterly_measure_links",
                columns: new[] { "CompanyId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_measure_links_ObjectiveId_SnapshotId",
                table: "quarterly_measure_links",
                columns: new[] { "ObjectiveId", "SnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_milestones_CompanyId_ObjectiveId",
                table: "quarterly_milestones",
                columns: new[] { "CompanyId", "ObjectiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_objectives_CompanyId_GoalId",
                table: "quarterly_objectives",
                columns: new[] { "CompanyId", "GoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_objectives_CompanyId_ReviewId",
                table: "quarterly_objectives",
                columns: new[] { "CompanyId", "ReviewId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_objectives_ReviewId_GoalId",
                table: "quarterly_objectives",
                columns: new[] { "ReviewId", "GoalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_resource_allocations_CompanyId_GoalId",
                table: "quarterly_resource_allocations",
                columns: new[] { "CompanyId", "GoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_resource_allocations_CompanyId_ReviewId",
                table: "quarterly_resource_allocations",
                columns: new[] { "CompanyId", "ReviewId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_resource_allocations_CompanyId_SnapshotId",
                table: "quarterly_resource_allocations",
                columns: new[] { "CompanyId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_reviews_CompanyId_FiscalYear_Quarter_Revision",
                table: "quarterly_reviews",
                columns: new[] { "CompanyId", "FiscalYear", "Quarter", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_reviews_CompanyId_PreviousId",
                table: "quarterly_reviews",
                columns: new[] { "CompanyId", "PreviousId" },
                unique: true,
                filter: "[PreviousId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_quarterly_reviews_CompanyId_RequestId",
                table: "quarterly_reviews",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quarterly_initiative_links");

            migrationBuilder.DropTable(
                name: "quarterly_measure_links");

            migrationBuilder.DropTable(
                name: "quarterly_milestones");

            migrationBuilder.DropTable(
                name: "quarterly_resource_allocations");

            migrationBuilder.DropTable(
                name: "quarterly_objectives");

            migrationBuilder.DropTable(
                name: "quarterly_reviews");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_operating_initiatives_company_id_id",
                table: "operating_initiatives");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_monthly_review_snapshots_CompanyId_Id",
                table: "monthly_review_snapshots");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_company_goals_company_id_id",
                table: "company_goals");
        }
    }
}
