using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413130000_AddToolExecutionMetadata")]
public partial class AddToolExecutionMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TaskId",
            table: "tool_execution_attempts",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "WorkflowInstanceId",
            table: "tool_execution_attempts",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CorrelationId",
            table: "tool_execution_attempts",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "StartedUtc",
            table: "tool_execution_attempts",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AddColumn<DateTime>(
            name: "CompletedUtc",
            table: "tool_execution_attempts",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_tool_execution_attempts_CompanyId_CorrelationId",
            table: "tool_execution_attempts",
            columns: new[] { "CompanyId", "CorrelationId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tool_execution_attempts_CompanyId_CorrelationId",
            table: "tool_execution_attempts");

        migrationBuilder.DropColumn(
            name: "CompletedUtc",
            table: "tool_execution_attempts");

        migrationBuilder.DropColumn(
            name: "StartedUtc",
            table: "tool_execution_attempts");

        migrationBuilder.DropColumn(
            name: "CorrelationId",
            table: "tool_execution_attempts");

        migrationBuilder.DropColumn(
            name: "WorkflowInstanceId",
            table: "tool_execution_attempts");

        migrationBuilder.DropColumn(
            name: "TaskId",
            table: "tool_execution_attempts");
    }
}
