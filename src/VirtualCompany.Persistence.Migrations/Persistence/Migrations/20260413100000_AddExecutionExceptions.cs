using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413100000_AddExecutionExceptions")]
public partial class AddExecutionExceptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var stringType = isPostgres ? "character varying" : "nvarchar";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";

        migrationBuilder.CreateTable(
            name: "execution_exceptions",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                kind = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false),
                severity = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false, defaultValue: "open"),
                title = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: false),
                summary = table.Column<string>(type: $"{stringType}(2000)", maxLength: 2000, nullable: false),
                source_type = table.Column<string>(type: $"{stringType}(64)", maxLength: 64, nullable: false),
                source_id = table.Column<string>(type: $"{stringType}(128)", maxLength: 128, nullable: false),
                background_execution_id = table.Column<Guid>(type: guidType, nullable: true),
                related_entity_type = table.Column<string>(type: $"{stringType}(100)", maxLength: 100, nullable: true),
                related_entity_id = table.Column<string>(type: $"{stringType}(128)", maxLength: 128, nullable: true),
                incident_key = table.Column<string>(type: $"{stringType}(300)", maxLength: 300, nullable: false),
                failure_code = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: true),
                details_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                resolved_at = table.Column<DateTime>(type: dateTimeType, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_execution_exceptions", x => x.id);
                table.ForeignKey(
                    name: "FK_execution_exceptions_background_executions_background_execution_id",
                    column: x => x.background_execution_id,
                    principalTable: "background_executions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_execution_exceptions_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_execution_exceptions_background_execution_id",
            table: "execution_exceptions",
            column: "background_execution_id");

        migrationBuilder.CreateIndex(
            name: "IX_execution_exceptions_company_id_incident_key",
            table: "execution_exceptions",
            columns: new[] { "company_id", "incident_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_execution_exceptions_company_id_kind_status_created_at",
            table: "execution_exceptions",
            columns: new[] { "company_id", "kind", "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_execution_exceptions_company_id_source_type_source_id",
            table: "execution_exceptions",
            columns: new[] { "company_id", "source_type", "source_id" });

        migrationBuilder.CreateIndex(
            name: "IX_execution_exceptions_company_id_status_created_at",
            table: "execution_exceptions",
            columns: new[] { "company_id", "status", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "execution_exceptions");
    }
}