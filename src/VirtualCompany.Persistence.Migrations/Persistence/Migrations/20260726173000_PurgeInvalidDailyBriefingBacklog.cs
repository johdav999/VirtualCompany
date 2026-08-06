using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260726173000_PurgeInvalidDailyBriefingBacklog")]
public partial class PurgeInvalidDailyBriefingBacklog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @invalid_workflow_instances TABLE
            (
                id uniqueidentifier NOT NULL PRIMARY KEY
            );

            INSERT INTO @invalid_workflow_instances (id)
            SELECT wi.id
            FROM workflow_instances AS wi
            INNER JOIN workflow_definitions AS wd
                ON wd.id = wi.definition_id
            WHERE wd.code = 'DAILY-EXECUTIVE-BRIEFING'
              AND wi.trigger_source = 'schedule'
              AND wi.state = 'cancelled'
              AND JSON_VALUE(wi.output_payload, '$.cancellationReason')
                  LIKE 'Cancelled invalid minute-generated daily briefing occurrence%'
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM tasks AS task
                  WHERE task.workflow_instance_id = wi.id
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM workflow_exceptions AS workflow_exception
                  WHERE workflow_exception.workflow_instance_id = wi.id
              );

            DELETE background_execution
            FROM background_executions AS background_execution
            INNER JOIN @invalid_workflow_instances AS invalid
                ON background_execution.related_entity_id =
                    REPLACE(CONVERT(nvarchar(36), invalid.id), '-', '')
            WHERE background_execution.related_entity_type = 'workflow_instance';

            DELETE workflow_instance
            FROM workflow_instances AS workflow_instance
            INNER JOIN @invalid_workflow_instances AS invalid
                ON invalid.id = workflow_instance.id;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Purged workflow and execution history cannot be reconstructed.
    }
}
