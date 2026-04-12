using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class ConvertAgentProfileJsonColumnsToJsonb : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        migrationBuilder.Sql(
            """
            ALTER TABLE agent_templates
            ALTER COLUMN personality_json TYPE jsonb
            USING CASE
                WHEN personality_json IS NULL OR btrim(personality_json) = '' THEN '{}'::jsonb
                ELSE personality_json::jsonb
            END;
            ALTER TABLE agent_templates ALTER COLUMN personality_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agent_templates
            ALTER COLUMN objectives_json TYPE jsonb
            USING CASE
                WHEN objectives_json IS NULL OR btrim(objectives_json) = '' THEN '{}'::jsonb
                ELSE objectives_json::jsonb
            END;
            ALTER TABLE agent_templates ALTER COLUMN objectives_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agent_templates
            ALTER COLUMN kpis_json TYPE jsonb
            USING CASE
                WHEN kpis_json IS NULL OR btrim(kpis_json) = '' THEN '{}'::jsonb
                ELSE kpis_json::jsonb
            END;
            ALTER TABLE agent_templates ALTER COLUMN kpis_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agent_templates
            ALTER COLUMN tool_permissions_json TYPE jsonb
            USING CASE
                WHEN tool_permissions_json IS NULL OR btrim(tool_permissions_json) = '' THEN '{}'::jsonb
                ELSE tool_permissions_json::jsonb
            END;
            ALTER TABLE agent_templates ALTER COLUMN tool_permissions_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agent_templates
            ALTER COLUMN data_scopes_json TYPE jsonb
            USING CASE
                WHEN data_scopes_json IS NULL OR btrim(data_scopes_json) = '' THEN '{}'::jsonb
                ELSE data_scopes_json::jsonb
            END;
            ALTER TABLE agent_templates ALTER COLUMN data_scopes_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agent_templates
            ALTER COLUMN approval_thresholds_json TYPE jsonb
            USING CASE
                WHEN approval_thresholds_json IS NULL OR btrim(approval_thresholds_json) = '' THEN '{}'::jsonb
                ELSE approval_thresholds_json::jsonb
            END;
            ALTER TABLE agent_templates ALTER COLUMN approval_thresholds_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agent_templates
            ALTER COLUMN escalation_rules_json TYPE jsonb
            USING CASE
                WHEN escalation_rules_json IS NULL OR btrim(escalation_rules_json) = '' THEN '{}'::jsonb
                ELSE escalation_rules_json::jsonb
            END;
            ALTER TABLE agent_templates ALTER COLUMN escalation_rules_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN personality_json TYPE jsonb
            USING CASE
                WHEN personality_json IS NULL OR btrim(personality_json) = '' THEN '{}'::jsonb
                ELSE personality_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN personality_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN objectives_json TYPE jsonb
            USING CASE
                WHEN objectives_json IS NULL OR btrim(objectives_json) = '' THEN '{}'::jsonb
                ELSE objectives_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN objectives_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN kpis_json TYPE jsonb
            USING CASE
                WHEN kpis_json IS NULL OR btrim(kpis_json) = '' THEN '{}'::jsonb
                ELSE kpis_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN kpis_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN tool_permissions_json TYPE jsonb
            USING CASE
                WHEN tool_permissions_json IS NULL OR btrim(tool_permissions_json) = '' THEN '{}'::jsonb
                ELSE tool_permissions_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN tool_permissions_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN data_scopes_json TYPE jsonb
            USING CASE
                WHEN data_scopes_json IS NULL OR btrim(data_scopes_json) = '' THEN '{}'::jsonb
                ELSE data_scopes_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN data_scopes_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN approval_thresholds_json TYPE jsonb
            USING CASE
                WHEN approval_thresholds_json IS NULL OR btrim(approval_thresholds_json) = '' THEN '{}'::jsonb
                ELSE approval_thresholds_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN approval_thresholds_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN escalation_rules_json TYPE jsonb
            USING CASE
                WHEN escalation_rules_json IS NULL OR btrim(escalation_rules_json) = '' THEN '{}'::jsonb
                ELSE escalation_rules_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN escalation_rules_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN trigger_logic_json TYPE jsonb
            USING CASE
                WHEN trigger_logic_json IS NULL OR btrim(trigger_logic_json) = '' THEN '{}'::jsonb
                ELSE trigger_logic_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN trigger_logic_json SET DEFAULT '{}'::jsonb;

            ALTER TABLE agents
            ALTER COLUMN working_hours_json TYPE jsonb
            USING CASE
                WHEN working_hours_json IS NULL OR btrim(working_hours_json) = '' THEN '{}'::jsonb
                ELSE working_hours_json::jsonb
            END;
            ALTER TABLE agents ALTER COLUMN working_hours_json SET DEFAULT '{}'::jsonb;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        migrationBuilder.Sql(
            """
            ALTER TABLE agent_templates ALTER COLUMN personality_json TYPE text USING personality_json::text;
            ALTER TABLE agent_templates ALTER COLUMN objectives_json TYPE text USING objectives_json::text;
            ALTER TABLE agent_templates ALTER COLUMN kpis_json TYPE text USING kpis_json::text;
            ALTER TABLE agent_templates ALTER COLUMN tool_permissions_json TYPE text USING tool_permissions_json::text;
            ALTER TABLE agent_templates ALTER COLUMN data_scopes_json TYPE text USING data_scopes_json::text;
            ALTER TABLE agent_templates ALTER COLUMN approval_thresholds_json TYPE text USING approval_thresholds_json::text;
            ALTER TABLE agent_templates ALTER COLUMN escalation_rules_json TYPE text USING escalation_rules_json::text;
            ALTER TABLE agents ALTER COLUMN personality_json TYPE text USING personality_json::text;
            ALTER TABLE agents ALTER COLUMN objectives_json TYPE text USING objectives_json::text;
            ALTER TABLE agents ALTER COLUMN kpis_json TYPE text USING kpis_json::text;
            ALTER TABLE agents ALTER COLUMN tool_permissions_json TYPE text USING tool_permissions_json::text;
            ALTER TABLE agents ALTER COLUMN data_scopes_json TYPE text USING data_scopes_json::text;
            ALTER TABLE agents ALTER COLUMN approval_thresholds_json TYPE text USING approval_thresholds_json::text;
            ALTER TABLE agents ALTER COLUMN escalation_rules_json TYPE text USING escalation_rules_json::text;
            ALTER TABLE agents ALTER COLUMN trigger_logic_json TYPE text USING trigger_logic_json::text;
            ALTER TABLE agents ALTER COLUMN working_hours_json TYPE text USING working_hours_json::text;
            """);
    }
}
