using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260412191000_AddWorkflowExceptions")]
public partial class AddWorkflowExceptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var stringType = isPostgres ? "character varying" : "nvarchar";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var openFilter = isPostgres ? "status = 'open'" : "[status] = N'open'";

        migrationBuilder.CreateTable(
            name: "workflow_exceptions",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                workflow_instance_id = table.Column<Guid>(type: guidType, nullable: false),
                workflow_definition_id = table.Column<Guid>(type: guidType, nullable: false),
                step_key = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: false),
                exception_type = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false, defaultValue: "open"),
                title = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: false),
                details = table.Column<string>(type: $"{stringType}(4000)", maxLength: 4000, nullable: false),
                error_code = table.Column<string>(type: $"{stringType}(100)", maxLength: 100, nullable: true),
                technical_details_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                occurred_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                reviewed_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                reviewed_by_user_id = table.Column<Guid>(type: guidType, nullable: true),
                resolution_notes = table.Column<string>(type: $"{stringType}(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workflow_exceptions", x => x.id);
                table.ForeignKey(
                    name: "FK_workflow_exceptions_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_workflow_exceptions_workflow_definitions_workflow_definition_id",
                    column: x => x.workflow_definition_id,
                    principalTable: "workflow_definitions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_workflow_exceptions_workflow_instances_workflow_instance_id",
                    column: x => x.workflow_instance_id,
                    principalTable: "workflow_instances",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_exceptions_company_id_status_occurred_at",
            table: "workflow_exceptions",
            columns: new[] { "company_id", "status", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_exceptions_company_id_workflow_instance_id_occurred_at",
            table: "workflow_exceptions",
            columns: new[] { "company_id", "workflow_instance_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_exceptions_company_id_workflow_instance_id_step_key_exception_type_status",
            table: "workflow_exceptions",
            columns: new[] { "company_id", "workflow_instance_id", "step_key", "exception_type", "status" },
            unique: true,
            filter: openFilter);

        migrationBuilder.CreateIndex(
            name: "IX_workflow_exceptions_workflow_definition_id",
            table: "workflow_exceptions",
            column: "workflow_definition_id");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_exceptions_workflow_instance_id",
            table: "workflow_exceptions",
            column: "workflow_instance_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "workflow_exceptions");
    }
}
