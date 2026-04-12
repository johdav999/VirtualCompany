using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260412183000_AddWorkflowDefinitionVersioning")]
public partial class AddWorkflowDefinitionVersioning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var stringType = isPostgres ? "character varying" : "nvarchar";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var boolType = isPostgres ? "boolean" : "bit";
        var boolDefault = isPostgres ? "TRUE" : "CAST(1 AS bit)";

        migrationBuilder.CreateTable(
            name: "workflow_definitions",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: true),
                code = table.Column<string>(type: $"{stringType}(100)", maxLength: 100, nullable: false),
                name = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: false),
                department = table.Column<string>(type: $"{stringType}(100)", maxLength: 100, nullable: true),
                version = table.Column<int>(type: "int", nullable: false),
                trigger_type = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false, defaultValue: "manual"),
                definition_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                active = table.Column<bool>(type: boolType, nullable: false, defaultValueSql: boolDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workflow_definitions", x => x.id);
                table.ForeignKey(
                    name: "FK_workflow_definitions_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_definitions_company_id_active",
            table: "workflow_definitions",
            columns: new[] { "company_id", "active" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_definitions_company_id_code",
            table: "workflow_definitions",
            columns: new[] { "company_id", "code" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_definitions_company_id_code_version",
            table: "workflow_definitions",
            columns: new[] { "company_id", "code", "version" },
            unique: true,
            filter: isPostgres ? "company_id IS NOT NULL" : "[company_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_definitions_system_code_version",
            table: "workflow_definitions",
            columns: new[] { "code", "version" },
            unique: true,
            filter: isPostgres ? "company_id IS NULL" : "[company_id] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_definitions_active_company_code",
            table: "workflow_definitions",
            columns: new[] { "company_id", "code" },
            unique: true,
            filter: isPostgres ? "company_id IS NOT NULL AND active = TRUE" : "[company_id] IS NOT NULL AND [active] = CAST(1 AS bit)");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_definitions_active_system_code",
            table: "workflow_definitions",
            column: "code",
            unique: true,
            filter: isPostgres ? "company_id IS NULL AND active = TRUE" : "[company_id] IS NULL AND [active] = CAST(1 AS bit)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "workflow_definitions");
    }
}
