using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaborationContributionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collaboration_contributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Pattern = table.Column<int>(type: "int", nullable: false),
                    Objective = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewOutcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaboration_contributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collaboration_contributions_agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collaboration_contributions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collaboration_contributions_tasks_ParentTaskId",
                        column: x => x.ParentTaskId,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collaboration_contributions_tasks_SourceTaskId",
                        column: x => x.SourceTaskId,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collaboration_execution_leases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaboration_execution_leases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collaboration_execution_leases_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collaboration_artifact_handoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivingContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaboration_artifact_handoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collaboration_artifact_handoffs_collaboration_contributions_InputContributionId",
                        column: x => x.InputContributionId,
                        principalTable: "collaboration_contributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collaboration_artifact_handoffs_collaboration_contributions_ReceivingContributionId",
                        column: x => x.ReceivingContributionId,
                        principalTable: "collaboration_contributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collaboration_artifact_handoffs_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_artifact_handoffs_CompanyId_InputContributionId_ReceivingContributionId",
                table: "collaboration_artifact_handoffs",
                columns: new[] { "CompanyId", "InputContributionId", "ReceivingContributionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_artifact_handoffs_InputContributionId",
                table: "collaboration_artifact_handoffs",
                column: "InputContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_artifact_handoffs_ReceivingContributionId",
                table: "collaboration_artifact_handoffs",
                column: "ReceivingContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_contributions_AgentId",
                table: "collaboration_contributions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_contributions_CompanyId_ParentTaskId_Sequence_Version",
                table: "collaboration_contributions",
                columns: new[] { "CompanyId", "ParentTaskId", "Sequence", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_contributions_ParentTaskId",
                table: "collaboration_contributions",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_contributions_SourceTaskId",
                table: "collaboration_contributions",
                column: "SourceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_execution_leases_CompanyId_Key",
                table: "collaboration_execution_leases",
                columns: new[] { "CompanyId", "Key" },
                unique: true);

            // Preserve permitted pre-P11 worker outputs as the first retained version. No
            // historical handoff, role, review or version sequence is inferred from prose.
            migrationBuilder.Sql("""
                UPDATE parent SET parent_task_id = source.id
                FROM tasks parent JOIN tasks source
                  ON source.company_id = parent.company_id
                 AND source.id = TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(parent.input_payload)=1 THEN parent.input_payload ELSE '{}' END, '$.sourceTaskId'))
                WHERE parent.type = 'manager_worker_collaboration' AND parent.parent_task_id IS NULL
                  AND source.id <> parent.id AND source.type NOT LIKE 'manager_worker%';

                WITH retained AS (
                    SELECT p.id ParentId, p.company_id CompanyId, w.id SourceId, a.Id AgentId,
                      COALESCE(TRY_CONVERT(uniqueidentifier, JSON_VALUE(p.input_payload,'$.planId')),
                        TRY_CONVERT(uniqueidentifier, STUFF(STUFF(STUFF(STUFF(JSON_VALUE(p.input_payload,'$.planId'),9,0,'-'),14,0,'-'),19,0,'-'),24,0,'-'))) PlanId,
                      COALESCE(c.Sequence,c.LowerSequence) Sequence,
                      LEFT(COALESCE(NULLIF(w.description,''),w.title),2000) Objective,
                      COALESCE(c.Status,c.LowerStatus) Status,
                      COALESCE(c.Output,c.LowerOutput,'') Output,
                      COALESCE(c.Rationale,c.LowerRationale,'') Rationale, w.updated_at ObservedUtc,
                      ROW_NUMBER() OVER (PARTITION BY p.id, COALESCE(c.Sequence,c.LowerSequence) ORDER BY w.id) rn
                    FROM tasks p
                    CROSS APPLY OPENJSON(CASE WHEN ISJSON(p.output_payload)=1 THEN p.output_payload ELSE '{}' END,'$.contributions')
                    WITH (SubtaskId uniqueidentifier '$.SubtaskId', LowerSubtaskId uniqueidentifier '$.subtaskId',
                          AgentId uniqueidentifier '$.AgentId', LowerAgentId uniqueidentifier '$.agentId',
                          Sequence int '$.Sequence', LowerSequence int '$.sequence',
                          Status nvarchar(32) '$.Status', LowerStatus nvarchar(32) '$.status',
                          Output nvarchar(max) '$.Output', LowerOutput nvarchar(max) '$.output',
                          Rationale nvarchar(2000) '$.RationaleSummary', LowerRationale nvarchar(2000) '$.rationaleSummary') c
                    JOIN tasks w ON w.id=COALESCE(c.SubtaskId,c.LowerSubtaskId) AND w.company_id=p.company_id AND w.parent_task_id=p.id
                    JOIN agents a ON a.Id=COALESCE(c.AgentId,c.LowerAgentId) AND a.CompanyId=p.company_id AND w.assigned_agent_id=a.Id
                    WHERE p.type='manager_worker_collaboration' AND ISJSON(p.input_payload)=1
                )
                INSERT INTO collaboration_contributions
                    (Id,CompanyId,ParentTaskId,SourceTaskId,PlanId,AgentId,Sequence,Version,Role,Pattern,Objective,Status,Output,Rationale,ReviewOutcome,CreatedUtc)
                SELECT NEWID(),CompanyId,ParentId,SourceId,PlanId,AgentId,Sequence,1,1,1,Objective,
                    CASE WHEN Status IN ('completed','failed','blocked','awaiting_approval','needs_review') THEN Status ELSE 'blocked' END,
                    CASE WHEN Status='failed' THEN '' ELSE Output END,
                    CASE WHEN Status='failed' THEN 'Imported failure; review the owning work before recovery.'
                         ELSE LEFT('Imported retained output; earlier version history and handoffs are not recorded. '+Rationale,2000) END,
                    NULL,ObservedUtc
                FROM retained WHERE rn=1 AND PlanId IS NOT NULL AND Sequence>0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collaboration_artifact_handoffs");

            migrationBuilder.DropTable(
                name: "collaboration_execution_leases");

            migrationBuilder.DropTable(
                name: "collaboration_contributions");
        }
    }
}
