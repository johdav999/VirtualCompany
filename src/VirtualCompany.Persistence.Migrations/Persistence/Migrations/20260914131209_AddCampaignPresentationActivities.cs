using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignPresentationActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_campaign_presentation_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_activity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preset_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    presenter_strategy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    explicit_presenter_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    work_strategy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    allow_overrides = table.Column<bool>(type: "bit", nullable: false),
                    event_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    preparation_lead_time_hours = table.Column<int>(type: "int", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_presentation_activities", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_presentation_activities_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_campaign_presentation_lead_time", "preparation_lead_time_hours BETWEEN 0 AND 2160");
                    table.ForeignKey(
                        name: "FK_sales_campaign_presentation_activities_agents_company_id_explicit_presenter_agent_id",
                        columns: x => new { x.company_id, x.explicit_presenter_agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_campaign_presentation_activities_sales_campaign_activities_company_id_sales_campaign_activity_id",
                        columns: x => new { x.company_id, x.sales_campaign_activity_id },
                        principalTable: "sales_campaign_activities",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_campaign_presentation_activities_sales_meeting_sessions_company_id_event_session_id",
                        columns: x => new { x.company_id, x.event_session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_campaign_presentation_activities_sales_presentation_preset_versions_company_id_preset_version_id",
                        columns: x => new { x.company_id, x.preset_version_id },
                        principalTable: "sales_presentation_preset_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_presentation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    configuration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    presentation_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_presentation_runs", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_presentation_runs_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_campaign_presentation_runs_sales_campaign_presentation_activities_company_id_configuration_id",
                        columns: x => new { x.company_id, x.configuration_id },
                        principalTable: "sales_campaign_presentation_activities",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_campaign_presentation_runs_sales_presentation_runs_company_id_presentation_run_id",
                        columns: x => new { x.company_id, x.presentation_run_id },
                        principalTable: "sales_presentation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_presentation_activities_company_id_event_session_id",
                table: "sales_campaign_presentation_activities",
                columns: new[] { "company_id", "event_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_presentation_activities_company_id_explicit_presenter_agent_id",
                table: "sales_campaign_presentation_activities",
                columns: new[] { "company_id", "explicit_presenter_agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_presentation_activities_company_id_preset_version_id",
                table: "sales_campaign_presentation_activities",
                columns: new[] { "company_id", "preset_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_presentation_activities_company_id_sales_campaign_activity_id",
                table: "sales_campaign_presentation_activities",
                columns: new[] { "company_id", "sales_campaign_activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_presentation_runs_company_id_configuration_id_status",
                table: "sales_campaign_presentation_runs",
                columns: new[] { "company_id", "configuration_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_presentation_runs_company_id_idempotency_key",
                table: "sales_campaign_presentation_runs",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_presentation_runs_company_id_presentation_run_id",
                table: "sales_campaign_presentation_runs",
                columns: new[] { "company_id", "presentation_run_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_campaign_presentation_runs");

            migrationBuilder.DropTable(
                name: "sales_campaign_presentation_activities");
        }
    }
}
