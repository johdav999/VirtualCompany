using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260726170000_CancelInvalidDailyBriefingBacklog")]
public partial class CancelInvalidDailyBriefingBacklog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE wi
            SET
                wi.state = 'cancelled',
                wi.updated_at = SYSUTCDATETIME(),
                wi.completed_at = SYSUTCDATETIME(),
                wi.output_payload = JSON_MODIFY(
                    COALESCE(NULLIF(wi.output_payload, ''), '{}'),
                    '$.cancellationReason',
                    'Cancelled invalid minute-generated daily briefing occurrence')
            FROM workflow_instances AS wi
            INNER JOIN workflow_definitions AS wd
                ON wd.id = wi.definition_id
            WHERE wd.code = 'DAILY-EXECUTIVE-BRIEFING'
              AND wi.trigger_source = 'schedule'
              AND wi.state IN ('started', 'running');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The prior runnable state and current execution validity cannot be
        // reconstructed safely. Retain the auditable cancelled records.
    }
}
