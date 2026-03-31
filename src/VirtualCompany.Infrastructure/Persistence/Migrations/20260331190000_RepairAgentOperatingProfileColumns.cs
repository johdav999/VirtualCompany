using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260331190000_RepairAgentOperatingProfileColumns")]
public partial class RepairAgentOperatingProfileColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('agents', 'role_brief') IS NULL
            BEGIN
                ALTER TABLE [agents] ADD [role_brief] nvarchar(4000) NULL;
            END;

            IF COL_LENGTH('agents', 'trigger_logic_json') IS NULL
            BEGIN
                ALTER TABLE [agents] ADD [trigger_logic_json] nvarchar(max) NOT NULL CONSTRAINT [DF_agents_trigger_logic_json_repair] DEFAULT N'{}';
            END;

            IF COL_LENGTH('agents', 'working_hours_json') IS NULL
            BEGIN
                ALTER TABLE [agents] ADD [working_hours_json] nvarchar(max) NOT NULL CONSTRAINT [DF_agents_working_hours_json_repair] DEFAULT N'{}';
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('agents', 'working_hours_json') IS NOT NULL
            BEGIN
                DECLARE @workingHoursConstraint sysname;
                SELECT @workingHoursConstraint = [dc].[name]
                FROM sys.default_constraints AS [dc]
                INNER JOIN sys.columns AS [c]
                    ON [c].[default_object_id] = [dc].[object_id]
                INNER JOIN sys.tables AS [t]
                    ON [t].[object_id] = [c].[object_id]
                WHERE [t].[name] = 'agents' AND [c].[name] = 'working_hours_json';

                IF @workingHoursConstraint IS NOT NULL
                BEGIN
                    EXEC(N'ALTER TABLE [agents] DROP CONSTRAINT [' + @workingHoursConstraint + ']');
                END;

                ALTER TABLE [agents] DROP COLUMN [working_hours_json];
            END;

            IF COL_LENGTH('agents', 'trigger_logic_json') IS NOT NULL
            BEGIN
                DECLARE @triggerLogicConstraint sysname;
                SELECT @triggerLogicConstraint = [dc].[name]
                FROM sys.default_constraints AS [dc]
                INNER JOIN sys.columns AS [c]
                    ON [c].[default_object_id] = [dc].[object_id]
                INNER JOIN sys.tables AS [t]
                    ON [t].[object_id] = [c].[object_id]
                WHERE [t].[name] = 'agents' AND [c].[name] = 'trigger_logic_json';

                IF @triggerLogicConstraint IS NOT NULL
                BEGIN
                    EXEC(N'ALTER TABLE [agents] DROP CONSTRAINT [' + @triggerLogicConstraint + ']');
                END;

                ALTER TABLE [agents] DROP COLUMN [trigger_logic_json];
            END;

            IF COL_LENGTH('agents', 'role_brief') IS NOT NULL
            BEGIN
                ALTER TABLE [agents] DROP COLUMN [role_brief];
            END;
            """);
    }
}
