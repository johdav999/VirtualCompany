using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260414110000_AddAgentInitiatedTaskCreation")]
    public partial class AddAgentInitiatedTaskCreation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
            var string2000Type = isPostgres ? "character varying(2000)" : "nvarchar(2000)";
            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "tasks",
                type: string64Type,
                maxLength: 64,
                nullable: false,
                defaultValue: "user");

            migrationBuilder.AddColumn<Guid>(
                name: "originating_agent_id",
                table: "tasks",
                type: guidType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trigger_source",
                table: "tasks",
                type: string128Type,
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "creation_reason",
                table: "tasks",
                type: string2000Type,
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trigger_event_id",
                table: "tasks",
                type: string200Type,
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_originating_agent_id_created_at",
                table: "tasks",
                columns: new[] { "company_id", "originating_agent_id", "created_at" });

            if (isPostgres)
            {
                migrationBuilder.Sql(
                    """
                    DO $$
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = 'public'
                              AND table_name = 'tasks'
                              AND column_name = 'correlation_id') THEN
                            ALTER TABLE tasks ADD COLUMN correlation_id character varying(128) NULL;
                        END IF;
                    END
                    $$;
                    """);

                migrationBuilder.Sql(
                    """
                    DO $$
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM pg_indexes
                            WHERE schemaname = 'public'
                              AND tablename = 'tasks'
                              AND indexname = 'IX_tasks_company_id_trigger_source_trigger_event_id_correlation_id_created_at') THEN
                            CREATE INDEX "IX_tasks_company_id_trigger_source_trigger_event_id_correlation_id_created_at"
                            ON tasks (company_id, trigger_source, trigger_event_id, correlation_id, created_at);
                        END IF;
                    END
                    $$;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH(N'[tasks]', N'correlation_id') IS NULL
                    BEGIN
                        ALTER TABLE [tasks] ADD [correlation_id] nvarchar(128) NULL;
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    IF NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'IX_tasks_company_id_trigger_source_trigger_event_id_correlation_id_created_at'
                          AND object_id = OBJECT_ID(N'[tasks]'))
                    BEGIN
                        CREATE INDEX [IX_tasks_company_id_trigger_source_trigger_event_id_correlation_id_created_at]
                        ON [tasks] ([company_id], [trigger_source], [trigger_event_id], [correlation_id], [created_at]);
                    END;
                    """);
            }
            migrationBuilder.CreateTable(
                name: "agent_task_creation_dedupe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    dedupe_key = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    task_id = table.Column<Guid>(type: guidType, nullable: false),
                    agent_id = table.Column<Guid>(type: guidType, nullable: false),
                    trigger_source = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    trigger_event_id = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    expires_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_task_creation_dedupe", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_task_creation_dedupe_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_agent_task_creation_dedupe_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_task_creation_dedupe_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_task_creation_dedupe_company_id_dedupe_key",
                table: "agent_task_creation_dedupe",
                columns: new[] { "company_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_task_creation_dedupe_company_id_expires_at",
                table: "agent_task_creation_dedupe",
                columns: new[] { "company_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_task_creation_dedupe_company_id_trigger_source_trigger_event_id_correlation_id",
                table: "agent_task_creation_dedupe",
                columns: new[] { "company_id", "trigger_source", "trigger_event_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_task_creation_dedupe_agent_id",
                table: "agent_task_creation_dedupe",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_task_creation_dedupe_task_id",
                table: "agent_task_creation_dedupe",
                column: "task_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "agent_task_creation_dedupe");

            migrationBuilder.DropIndex(
                name: "IX_tasks_company_id_originating_agent_id_created_at",
                table: "tasks");

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DROP INDEX IF EXISTS "IX_tasks_company_id_trigger_source_trigger_event_id_correlation_id_created_at";
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'IX_tasks_company_id_trigger_source_trigger_event_id_correlation_id_created_at'
                          AND object_id = OBJECT_ID(N'[tasks]'))
                    BEGIN
                        DROP INDEX [IX_tasks_company_id_trigger_source_trigger_event_id_correlation_id_created_at] ON [tasks];
                    END;
                    """);
            }

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "originating_agent_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "trigger_source",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "creation_reason",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "trigger_event_id",
                table: "tasks");
        }
    }
}
