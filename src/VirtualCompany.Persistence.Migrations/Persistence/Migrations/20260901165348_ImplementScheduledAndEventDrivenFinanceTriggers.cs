using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementScheduledAndEventDrivenFinanceTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allowed_event_types_json",
                table: "finance_autonomy_grant_versions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'[]'");

            migrationBuilder.AddColumn<string>(
                name: "catch_up_behavior",
                table: "finance_autonomy_grant_versions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "latest");

            migrationBuilder.AddColumn<int>(
                name: "debounce_minutes",
                table: "finance_autonomy_grant_versions",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "late_event_tolerance_minutes",
                table: "finance_autonomy_grant_versions",
                type: "int",
                nullable: false,
                defaultValue: 1440);

            migrationBuilder.AddColumn<int>(
                name: "maximum_catch_up_windows",
                table: "finance_autonomy_grant_versions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "maximum_runs_per_window",
                table: "finance_autonomy_grant_versions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "minimum_interval_minutes",
                table: "finance_autonomy_grant_versions",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateTable(
                name: "finance_autonomy_trigger_cursors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grant_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    capability_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    trigger_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    trigger_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    cursor_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_event_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    current_window_start_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    current_window_end_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    quota_window_start_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    quota_window_end_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    runs_in_window = table.Column<int>(type: "int", nullable: false),
                    last_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_run_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_eligible_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    lease_token = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    lease_expires_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_trigger_cursors", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_trigger_cursors_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_trigger_cursors_finance_autonomy_grant_versions_company_id_grant_version_id",
                        columns: x => new { x.company_id, x.grant_version_id },
                        principalTable: "finance_autonomy_grant_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_trigger_cursors_finance_autonomy_grants_company_id_grant_id",
                        columns: x => new { x.company_id, x.grant_id },
                        principalTable: "finance_autonomy_grants",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_trigger_cursors_finance_autonomy_runs_last_run_id",
                        column: x => x.last_run_id,
                        principalTable: "finance_autonomy_runs",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_trigger_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cursor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_event_id = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    source_event_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_entity_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_entity_id = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    occurred_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    evidence_observed_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    coalescing_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    safe_label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    processed_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_trigger_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_trigger_events_finance_autonomy_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "finance_autonomy_runs",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_trigger_events_finance_autonomy_trigger_cursors_company_id_cursor_id",
                        columns: x => new { x.company_id, x.cursor_id },
                        principalTable: "finance_autonomy_trigger_cursors",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_cursors_company_id_grant_id",
                table: "finance_autonomy_trigger_cursors",
                columns: new[] { "company_id", "grant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_cursors_company_id_grant_version_id_trigger_kind_trigger_key",
                table: "finance_autonomy_trigger_cursors",
                columns: new[] { "company_id", "grant_version_id", "trigger_kind", "trigger_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_cursors_company_id_status_updated_utc",
                table: "finance_autonomy_trigger_cursors",
                columns: new[] { "company_id", "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_cursors_last_run_id",
                table: "finance_autonomy_trigger_cursors",
                column: "last_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_cursors_status_next_eligible_utc_lease_expires_utc_updated_utc",
                table: "finance_autonomy_trigger_cursors",
                columns: new[] { "status", "next_eligible_utc", "lease_expires_utc", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_events_company_id_cursor_id_created_utc",
                table: "finance_autonomy_trigger_events",
                columns: new[] { "company_id", "cursor_id", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_events_company_id_cursor_id_source_event_id_source_event_version",
                table: "finance_autonomy_trigger_events",
                columns: new[] { "company_id", "cursor_id", "source_event_id", "source_event_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_events_company_id_status_occurred_utc",
                table: "finance_autonomy_trigger_events",
                columns: new[] { "company_id", "status", "occurred_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_trigger_events_run_id",
                table: "finance_autonomy_trigger_events",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_autonomy_trigger_events");

            migrationBuilder.DropTable(
                name: "finance_autonomy_trigger_cursors");

            migrationBuilder.DropColumn(
                name: "allowed_event_types_json",
                table: "finance_autonomy_grant_versions");

            migrationBuilder.DropColumn(
                name: "catch_up_behavior",
                table: "finance_autonomy_grant_versions");

            migrationBuilder.DropColumn(
                name: "debounce_minutes",
                table: "finance_autonomy_grant_versions");

            migrationBuilder.DropColumn(
                name: "late_event_tolerance_minutes",
                table: "finance_autonomy_grant_versions");

            migrationBuilder.DropColumn(
                name: "maximum_catch_up_windows",
                table: "finance_autonomy_grant_versions");

            migrationBuilder.DropColumn(
                name: "maximum_runs_per_window",
                table: "finance_autonomy_grant_versions");

            migrationBuilder.DropColumn(
                name: "minimum_interval_minutes",
                table: "finance_autonomy_grant_versions");
        }
    }
}
