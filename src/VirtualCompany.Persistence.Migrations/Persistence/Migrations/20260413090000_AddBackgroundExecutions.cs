using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413090000_AddBackgroundExecutions")]
public partial class AddBackgroundExecutions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var stringType = isPostgres ? "character varying" : "nvarchar";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var outboxStatusDefault = isPostgres ? "'pending'" : "N'pending'";

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "company_outbox_messages",
            type: $"{stringType}(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "pending");

        migrationBuilder.Sql("UPDATE company_outbox_messages SET Status = CASE WHEN ProcessedUtc IS NOT NULL AND LastError IS NOT NULL THEN 'failed' WHEN ProcessedUtc IS NOT NULL THEN 'dispatched' WHEN ClaimedUtc IS NOT NULL THEN 'in_progress' WHEN AttemptCount > 0 THEN 'retry_scheduled' ELSE 'pending' END");

        migrationBuilder.CreateTable(
            name: "background_executions",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                execution_type = table.Column<string>(type: $"{stringType}(64)", maxLength: 64, nullable: false),
                related_entity_type = table.Column<string>(type: $"{stringType}(100)", maxLength: 100, nullable: false),
                related_entity_id = table.Column<string>(type: $"{stringType}(128)", maxLength: 128, nullable: false),
                correlation_id = table.Column<string>(type: $"{stringType}(128)", maxLength: 128, nullable: false),
                idempotency_key = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                attempt_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                max_attempts = table.Column<int>(type: "int", nullable: false),
                next_retry_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                started_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                heartbeat_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                completed_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                failure_category = table.Column<string>(type: $"{stringType}(64)", maxLength: 64, nullable: true),
                failure_code = table.Column<string>(type: $"{stringType}(100)", maxLength: 100, nullable: true),
                failure_message = table.Column<string>(type: $"{stringType}(4000)", maxLength: 4000, nullable: true),
                escalation_id = table.Column<Guid>(type: guidType, nullable: true),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_background_executions", x => x.id);
                table.ForeignKey(
                    name: "FK_background_executions_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_company_outbox_messages_CompanyId_Status_AvailableUtc",
            table: "company_outbox_messages",
            columns: new[] { "CompanyId", "Status", "AvailableUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_background_executions_company_id_execution_type_idempotency_key",
            table: "background_executions",
            columns: new[] { "company_id", "execution_type", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_background_executions_company_id_related_entity_type_related_entity_id",
            table: "background_executions",
            columns: new[] { "company_id", "related_entity_type", "related_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_background_executions_company_id_status_next_retry_at",
            table: "background_executions",
            columns: new[] { "company_id", "status", "next_retry_at" });

        migrationBuilder.CreateIndex(
            name: "IX_background_executions_correlation_id",
            table: "background_executions",
            column: "correlation_id");

        migrationBuilder.CreateIndex(
            name: "IX_background_executions_status_heartbeat_at",
            table: "background_executions",
            columns: new[] { "status", "heartbeat_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "background_executions");
        migrationBuilder.DropIndex(
            name: "IX_company_outbox_messages_CompanyId_Status_AvailableUtc",
            table: "company_outbox_messages");
        migrationBuilder.DropColumn(
            name: "Status",
            table: "company_outbox_messages");
    }
}