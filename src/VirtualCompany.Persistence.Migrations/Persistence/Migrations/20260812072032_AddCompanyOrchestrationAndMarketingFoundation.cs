using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyOrchestrationAndMarketingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    metric_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    metric_unit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    baseline_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    target_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    start_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    target_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    constraints_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_goals", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_goals_agents_owner_agent_id",
                        column: x => x.owner_agent_id,
                        principalTable: "agents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_company_goals_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_goals_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "company_operating_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coordinator_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    autonomy_level = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    timezone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    daily_cycle_hour = table.Column<int>(type: "int", nullable: false),
                    minimum_cycle_interval_minutes = table.Column<int>(type: "int", nullable: false),
                    maximum_cycles_per_day = table.Column<int>(type: "int", nullable: false),
                    maximum_initiatives_per_cycle = table.Column<int>(type: "int", nullable: false),
                    maximum_tasks_per_cycle = table.Column<int>(type: "int", nullable: false),
                    maximum_collaborators = table.Column<int>(type: "int", nullable: false),
                    maximum_runtime_seconds = table.Column<int>(type: "int", nullable: false),
                    maximum_model_calls_per_cycle = table.Column<int>(type: "int", nullable: false),
                    maximum_tool_calls_per_cycle = table.Column<int>(type: "int", nullable: false),
                    maximum_monetary_budget_per_cycle = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    is_paused = table.Column<bool>(type: "bit", nullable: false),
                    pause_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_operating_configurations", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_operating_configurations_agents_coordinator_agent_id",
                        column: x => x.coordinator_agent_id,
                        principalTable: "agents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_company_operating_configurations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operating_cycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    trigger_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    trigger_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    coordinator_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    configuration_version = table.Column<int>(type: "int", nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    model_calls_used = table.Column<int>(type: "int", nullable: false),
                    tool_calls_used = table.Column<int>(type: "int", nullable: false),
                    tasks_created = table.Column<int>(type: "int", nullable: false),
                    monetary_budget_used = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_cycles", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_cycles_agents_coordinator_agent_id",
                        column: x => x.coordinator_agent_id,
                        principalTable: "agents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_operating_cycles_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operating_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supersedes_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    objective = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    rationale_summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    uncertainty_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    committed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_plans_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operating_plans_operating_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "operating_cycles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_plans_operating_plans_supersedes_plan_id",
                        column: x => x.supersedes_plan_id,
                        principalTable: "operating_plans",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "operating_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schema_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    source_count = table.Column<int>(type: "int", nullable: false),
                    data_gap_count = table.Column<int>(type: "int", nullable: false),
                    is_truncated = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_snapshots_operating_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "operating_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operating_initiatives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    goal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    desired_outcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    completion_evidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    owner_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    target_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    budget = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_initiatives", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_initiatives_agents_owner_agent_id",
                        column: x => x.owner_agent_id,
                        principalTable: "agents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_operating_initiatives_company_goals_goal_id",
                        column: x => x.goal_id,
                        principalTable: "company_goals",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_initiatives_operating_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "operating_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operating_initiatives_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_initiatives_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "operating_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action_class = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    proposed_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rationale_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    risk_level = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approval_required = table.Column<bool>(type: "bit", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_decisions_agents_proposed_agent_id",
                        column: x => x.proposed_agent_id,
                        principalTable: "agents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_operating_decisions_operating_initiatives_initiative_id",
                        column: x => x.initiative_id,
                        principalTable: "operating_initiatives",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_decisions_operating_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "operating_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operating_plan_dependencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    depends_on_initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_plan_dependencies", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_plan_dependencies_operating_initiatives_depends_on_initiative_id",
                        column: x => x.depends_on_initiative_id,
                        principalTable: "operating_initiatives",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_plan_dependencies_operating_initiatives_initiative_id",
                        column: x => x.initiative_id,
                        principalTable: "operating_initiatives",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_plan_dependencies_operating_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "operating_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operating_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    evidence_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_reviews_operating_initiatives_initiative_id",
                        column: x => x.initiative_id,
                        principalTable: "operating_initiatives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operating_validation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decision_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    validator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    validator_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    approval_required = table.Column<bool>(type: "bit", nullable: false),
                    configuration_version = table.Column<int>(type: "int", nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    evaluated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_validation_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_validation_results_operating_decisions_decision_id",
                        column: x => x.decision_id,
                        principalTable: "operating_decisions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_validation_results_operating_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "operating_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "agent_templates",
                keyColumn: "Id",
                keyValue: new Guid("3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30103"),
                columns: new[] { "data_scopes_json", "tool_permissions_json" },
                values: new object[] { "{\"read\":[\"marketing\",\"sales\",\"knowledge\"],\"recommend\":[\"marketing\",\"sales\",\"knowledge\"],\"execute\":[],\"write\":[]}", "{\"allowed\":[\"marketing.read_workspace\",\"marketing.read_objectives\",\"marketing.read_campaigns\",\"marketing.read_content_calendar\",\"marketing.read_audience_evidence\",\"marketing.read_channel_observations\",\"marketing.read_attribution_summary\",\"marketing.search_approved_knowledge\",\"marketing.read_segments\",\"marketing.read_segment_evidence\",\"marketing.prepare_plan\",\"marketing.analyze_audience\",\"marketing.prepare_content_brief\",\"marketing.recommend_campaign_change\",\"marketing.prepare_performance_review\",\"marketing.prepare_experiment\",\"marketing.prepare_operating_review\",\"marketing.prepare_segmentation\",\"marketing.recommend_target_segments\",\"marketing.assess_segment_strategy_impact\"],\"denied\":[],\"actions\":[\"read\",\"recommend\"],\"deniedActions\":[\"execute\"]}" });

            migrationBuilder.CreateIndex(
                name: "IX_company_goals_company_id_status_priority",
                table: "company_goals",
                columns: new[] { "company_id", "status", "priority" });

            migrationBuilder.CreateIndex(
                name: "IX_company_goals_company_id_target_at",
                table: "company_goals",
                columns: new[] { "company_id", "target_at" });

            migrationBuilder.CreateIndex(
                name: "IX_company_goals_owner_agent_id",
                table: "company_goals",
                column: "owner_agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_goals_owner_user_id",
                table: "company_goals",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_operating_configurations_company_id",
                table: "company_operating_configurations",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_operating_configurations_coordinator_agent_id",
                table: "company_operating_configurations",
                column: "coordinator_agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_cycles_company_id_idempotency_key",
                table: "operating_cycles",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_cycles_company_id_status_requested_at",
                table: "operating_cycles",
                columns: new[] { "company_id", "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_cycles_coordinator_agent_id",
                table: "operating_cycles",
                column: "coordinator_agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_decisions_company_id_idempotency_key",
                table: "operating_decisions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_decisions_initiative_id",
                table: "operating_decisions",
                column: "initiative_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_decisions_plan_id",
                table: "operating_decisions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_decisions_proposed_agent_id",
                table: "operating_decisions",
                column: "proposed_agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiatives_company_id_status_target_at",
                table: "operating_initiatives",
                columns: new[] { "company_id", "status", "target_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiatives_goal_id",
                table: "operating_initiatives",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiatives_owner_agent_id",
                table: "operating_initiatives",
                column: "owner_agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiatives_plan_id",
                table: "operating_initiatives",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiatives_task_id",
                table: "operating_initiatives",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiatives_workflow_instance_id",
                table: "operating_initiatives",
                column: "workflow_instance_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_plan_dependencies_company_id_plan_id_initiative_id_depends_on_initiative_id",
                table: "operating_plan_dependencies",
                columns: new[] { "company_id", "plan_id", "initiative_id", "depends_on_initiative_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_plan_dependencies_depends_on_initiative_id",
                table: "operating_plan_dependencies",
                column: "depends_on_initiative_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_plan_dependencies_initiative_id",
                table: "operating_plan_dependencies",
                column: "initiative_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_plan_dependencies_plan_id",
                table: "operating_plan_dependencies",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_plans_company_id_cycle_id_version",
                table: "operating_plans",
                columns: new[] { "company_id", "cycle_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_plans_cycle_id",
                table: "operating_plans",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_plans_supersedes_plan_id",
                table: "operating_plans",
                column: "supersedes_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_reviews_company_id_initiative_id_evidence_version",
                table: "operating_reviews",
                columns: new[] { "company_id", "initiative_id", "evidence_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_reviews_initiative_id",
                table: "operating_reviews",
                column: "initiative_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_snapshots_company_id_cycle_id",
                table: "operating_snapshots",
                columns: new[] { "company_id", "cycle_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_snapshots_cycle_id",
                table: "operating_snapshots",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_validation_results_company_id_plan_id_validator",
                table: "operating_validation_results",
                columns: new[] { "company_id", "plan_id", "validator" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_validation_results_decision_id",
                table: "operating_validation_results",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_validation_results_plan_id",
                table: "operating_validation_results",
                column: "plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_operating_configurations");

            migrationBuilder.DropTable(
                name: "operating_plan_dependencies");

            migrationBuilder.DropTable(
                name: "operating_reviews");

            migrationBuilder.DropTable(
                name: "operating_snapshots");

            migrationBuilder.DropTable(
                name: "operating_validation_results");

            migrationBuilder.DropTable(
                name: "operating_decisions");

            migrationBuilder.DropTable(
                name: "operating_initiatives");

            migrationBuilder.DropTable(
                name: "company_goals");

            migrationBuilder.DropTable(
                name: "operating_plans");

            migrationBuilder.DropTable(
                name: "operating_cycles");

            migrationBuilder.UpdateData(
                table: "agent_templates",
                keyColumn: "Id",
                keyValue: new Guid("3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30103"),
                columns: new[] { "data_scopes_json", "tool_permissions_json" },
                values: new object[] { "{\"read\":[\"campaigns\",\"analytics\",\"content_calendar\"],\"write\":[\"campaign_briefs\",\"draft_copy\",\"weekly_reports\"]}", "{\"allowed\":[\"analytics\",\"cms\",\"email_marketing\",\"ads_manager\"]}" });
        }
    }
}
