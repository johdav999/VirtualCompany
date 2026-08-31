using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingCloseOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_application_lines_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_application_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_evidence_links_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_evidence_links");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_template_lines_accounting_allocation_template_versions_company_id_template_version_id",
                table: "accounting_allocation_template_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_template_versions_accounting_allocation_templates_company_id_template_id",
                table: "accounting_allocation_template_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_dimension_account_policies_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_account_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_dimension_account_policies_finance_accounts_company_id_finance_account_id",
                table: "accounting_dimension_account_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_dimension_members_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_members");

            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entry_line_dimensions_ledger_entry_lines_company_id_ledger_entry_line_id",
                table: "ledger_entry_line_dimensions");

            migrationBuilder.DropForeignKey(
                name: "FK_manual_journal_draft_line_dimensions_manual_journal_draft_lines_company_id_manual_journal_draft_line_id",
                table: "manual_journal_draft_line_dimensions");

            migrationBuilder.CreateTable(
                name: "accounting_close_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_evidence_requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MinimumCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_evidence_requirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartIdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_instances_finance_fiscal_periods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalTable: "finance_fiscal_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_status_history_accounting_close_instances_CloseInstanceId",
                        column: x => x.CloseInstanceId,
                        principalTable: "accounting_close_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_task_blockers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SafeNextAction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_task_blockers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_task_definition_dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredecessorTaskDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DependentTaskDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_task_definition_dependencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_task_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DueOffsetDays = table.Column<int>(type: "int", nullable: false),
                    DefaultOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultOwnerRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequiresSignOff = table.Column<bool>(type: "bit", nullable: false),
                    SignOffRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MaterialityAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_task_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_task_dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredecessorTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DependentTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_task_dependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_task_dependencies_accounting_close_instances_CloseInstanceId",
                        column: x => x.CloseInstanceId,
                        principalTable: "accounting_close_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_task_evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LinkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_task_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_task_evidence_knowledge_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_task_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_task_notes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequiresSignOff = table.Column<bool>(type: "bit", nullable: false),
                    SignOffRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MaterialityAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    WorkTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_tasks_accounting_close_instances_CloseInstanceId",
                        column: x => x.CloseInstanceId,
                        principalTable: "accounting_close_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_close_tasks_accounting_close_task_definitions_TaskDefinitionId",
                        column: x => x.TaskDefinitionId,
                        principalTable: "accounting_close_task_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_tasks_approval_requests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalTable: "approval_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_tasks_tasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_template_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_template_history", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_template_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_template_sections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_template_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MaterialityAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    MaterialityPercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActivatedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RetiredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_template_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActiveVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LatestVersionNumber = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_templates_accounting_close_template_versions_ActiveVersionId",
                        column: x => x.ActiveVersionId,
                        principalTable: "accounting_close_template_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_templates_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_evidence_requirements_CompanyId_TaskDefinitionId_EvidenceType",
                table: "accounting_close_evidence_requirements",
                columns: new[] { "CompanyId", "TaskDefinitionId", "EvidenceType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_evidence_requirements_TaskDefinitionId",
                table: "accounting_close_evidence_requirements",
                column: "TaskDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_instances_CompanyId_FiscalPeriodId_TemplateVersionId",
                table: "accounting_close_instances",
                columns: new[] { "CompanyId", "FiscalPeriodId", "TemplateVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_instances_CompanyId_StartIdempotencyKey",
                table: "accounting_close_instances",
                columns: new[] { "CompanyId", "StartIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_instances_CompanyId_Status_UpdatedUtc",
                table: "accounting_close_instances",
                columns: new[] { "CompanyId", "Status", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_instances_FiscalPeriodId",
                table: "accounting_close_instances",
                column: "FiscalPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_instances_TemplateId",
                table: "accounting_close_instances",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_instances_TemplateVersionId",
                table: "accounting_close_instances",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_operations_CompanyId_IdempotencyKey",
                table: "accounting_close_operations",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_operations_CompanyId_TargetId_CreatedUtc",
                table: "accounting_close_operations",
                columns: new[] { "CompanyId", "TargetId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_status_history_CloseInstanceId",
                table: "accounting_close_status_history",
                column: "CloseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_status_history_CloseTaskId",
                table: "accounting_close_status_history",
                column: "CloseTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_status_history_CompanyId_CloseInstanceId_OccurredUtc",
                table: "accounting_close_status_history",
                columns: new[] { "CompanyId", "CloseInstanceId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_blockers_CloseTaskId",
                table: "accounting_close_task_blockers",
                column: "CloseTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_blockers_CompanyId_CloseTaskId_Status",
                table: "accounting_close_task_blockers",
                columns: new[] { "CompanyId", "CloseTaskId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definition_dependencies_CompanyId_DependentTaskDefinitionId",
                table: "accounting_close_task_definition_dependencies",
                columns: new[] { "CompanyId", "DependentTaskDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definition_dependencies_CompanyId_TemplateVersionId_PredecessorTaskDefinitionId_DependentTaskDefinitio~",
                table: "accounting_close_task_definition_dependencies",
                columns: new[] { "CompanyId", "TemplateVersionId", "PredecessorTaskDefinitionId", "DependentTaskDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definition_dependencies_DependentTaskDefinitionId",
                table: "accounting_close_task_definition_dependencies",
                column: "DependentTaskDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definition_dependencies_PredecessorTaskDefinitionId",
                table: "accounting_close_task_definition_dependencies",
                column: "PredecessorTaskDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definition_dependencies_TemplateVersionId",
                table: "accounting_close_task_definition_dependencies",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definitions_CompanyId_TemplateVersionId_Key",
                table: "accounting_close_task_definitions",
                columns: new[] { "CompanyId", "TemplateVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definitions_CompanyId_TemplateVersionId_Sequence",
                table: "accounting_close_task_definitions",
                columns: new[] { "CompanyId", "TemplateVersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definitions_SectionId",
                table: "accounting_close_task_definitions",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_definitions_TemplateVersionId",
                table: "accounting_close_task_definitions",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_dependencies_CloseInstanceId",
                table: "accounting_close_task_dependencies",
                column: "CloseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_dependencies_CompanyId_CloseInstanceId_PredecessorTaskId_DependentTaskId",
                table: "accounting_close_task_dependencies",
                columns: new[] { "CompanyId", "CloseInstanceId", "PredecessorTaskId", "DependentTaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_dependencies_CompanyId_DependentTaskId",
                table: "accounting_close_task_dependencies",
                columns: new[] { "CompanyId", "DependentTaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_dependencies_DependentTaskId",
                table: "accounting_close_task_dependencies",
                column: "DependentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_dependencies_PredecessorTaskId",
                table: "accounting_close_task_dependencies",
                column: "PredecessorTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_evidence_CloseTaskId",
                table: "accounting_close_task_evidence",
                column: "CloseTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_evidence_CompanyId_CloseTaskId_DocumentId_EvidenceType",
                table: "accounting_close_task_evidence",
                columns: new[] { "CompanyId", "CloseTaskId", "DocumentId", "EvidenceType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_evidence_DocumentId",
                table: "accounting_close_task_evidence",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_notes_CloseTaskId",
                table: "accounting_close_task_notes",
                column: "CloseTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_task_notes_CompanyId_CloseTaskId_CreatedUtc",
                table: "accounting_close_task_notes",
                columns: new[] { "CompanyId", "CloseTaskId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_ApprovalRequestId",
                table: "accounting_close_tasks",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_CloseInstanceId",
                table: "accounting_close_tasks",
                column: "CloseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_CompanyId_CloseInstanceId_Key",
                table: "accounting_close_tasks",
                columns: new[] { "CompanyId", "CloseInstanceId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_CompanyId_CloseInstanceId_Status",
                table: "accounting_close_tasks",
                columns: new[] { "CompanyId", "CloseInstanceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_CompanyId_OwnerUserId_Status_DueUtc",
                table: "accounting_close_tasks",
                columns: new[] { "CompanyId", "OwnerUserId", "Status", "DueUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_CompanyId_WorkTaskId",
                table: "accounting_close_tasks",
                columns: new[] { "CompanyId", "WorkTaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_SectionId",
                table: "accounting_close_tasks",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_TaskDefinitionId",
                table: "accounting_close_tasks",
                column: "TaskDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_tasks_WorkTaskId",
                table: "accounting_close_tasks",
                column: "WorkTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_history_CompanyId_TemplateId_OccurredUtc",
                table: "accounting_close_template_history",
                columns: new[] { "CompanyId", "TemplateId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_history_TemplateId",
                table: "accounting_close_template_history",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_history_TemplateVersionId",
                table: "accounting_close_template_history",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_sections_CompanyId_TemplateVersionId_Key",
                table: "accounting_close_template_sections",
                columns: new[] { "CompanyId", "TemplateVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_sections_CompanyId_TemplateVersionId_Sequence",
                table: "accounting_close_template_sections",
                columns: new[] { "CompanyId", "TemplateVersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_sections_TemplateVersionId",
                table: "accounting_close_template_sections",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_versions_CompanyId_TemplateId_Status",
                table: "accounting_close_template_versions",
                columns: new[] { "CompanyId", "TemplateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_versions_CompanyId_TemplateId_VersionNumber",
                table: "accounting_close_template_versions",
                columns: new[] { "CompanyId", "TemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_template_versions_TemplateId",
                table: "accounting_close_template_versions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_templates_ActiveVersionId",
                table: "accounting_close_templates",
                column: "ActiveVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_templates_CompanyId_Code",
                table: "accounting_close_templates",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_templates_CompanyId_Status_UpdatedUtc",
                table: "accounting_close_templates",
                columns: new[] { "CompanyId", "Status", "UpdatedUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_application_lines_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_application_lines",
                columns: new[] { "company_id", "application_id" },
                principalTable: "accounting_allocation_applications",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_evidence_links_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_evidence_links",
                columns: new[] { "company_id", "application_id" },
                principalTable: "accounting_allocation_applications",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_template_lines_accounting_allocation_template_versions_company_id_template_version_id",
                table: "accounting_allocation_template_lines",
                columns: new[] { "company_id", "template_version_id" },
                principalTable: "accounting_allocation_template_versions",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_template_versions_accounting_allocation_templates_company_id_template_id",
                table: "accounting_allocation_template_versions",
                columns: new[] { "company_id", "template_id" },
                principalTable: "accounting_allocation_templates",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_dimension_account_policies_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_account_policies",
                columns: new[] { "company_id", "dimension_type_id" },
                principalTable: "accounting_dimension_types",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_dimension_account_policies_finance_accounts_company_id_finance_account_id",
                table: "accounting_dimension_account_policies",
                columns: new[] { "company_id", "finance_account_id" },
                principalTable: "finance_accounts",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_dimension_members_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_members",
                columns: new[] { "company_id", "dimension_type_id" },
                principalTable: "accounting_dimension_types",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entry_line_dimensions_ledger_entry_lines_company_id_ledger_entry_line_id",
                table: "ledger_entry_line_dimensions",
                columns: new[] { "company_id", "ledger_entry_line_id" },
                principalTable: "ledger_entry_lines",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_manual_journal_draft_line_dimensions_manual_journal_draft_lines_company_id_manual_journal_draft_line_id",
                table: "manual_journal_draft_line_dimensions",
                columns: new[] { "company_id", "manual_journal_draft_line_id" },
                principalTable: "manual_journal_draft_lines",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_evidence_requirements_accounting_close_task_definitions_TaskDefinitionId",
                table: "accounting_close_evidence_requirements",
                column: "TaskDefinitionId",
                principalTable: "accounting_close_task_definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_instances_accounting_close_template_versions_TemplateVersionId",
                table: "accounting_close_instances",
                column: "TemplateVersionId",
                principalTable: "accounting_close_template_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_instances_accounting_close_templates_TemplateId",
                table: "accounting_close_instances",
                column: "TemplateId",
                principalTable: "accounting_close_templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_status_history_accounting_close_tasks_CloseTaskId",
                table: "accounting_close_status_history",
                column: "CloseTaskId",
                principalTable: "accounting_close_tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_blockers_accounting_close_tasks_CloseTaskId",
                table: "accounting_close_task_blockers",
                column: "CloseTaskId",
                principalTable: "accounting_close_tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_definition_dependencies_accounting_close_task_definitions_DependentTaskDefinitionId",
                table: "accounting_close_task_definition_dependencies",
                column: "DependentTaskDefinitionId",
                principalTable: "accounting_close_task_definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_definition_dependencies_accounting_close_task_definitions_PredecessorTaskDefinitionId",
                table: "accounting_close_task_definition_dependencies",
                column: "PredecessorTaskDefinitionId",
                principalTable: "accounting_close_task_definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_definition_dependencies_accounting_close_template_versions_TemplateVersionId",
                table: "accounting_close_task_definition_dependencies",
                column: "TemplateVersionId",
                principalTable: "accounting_close_template_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_definitions_accounting_close_template_sections_SectionId",
                table: "accounting_close_task_definitions",
                column: "SectionId",
                principalTable: "accounting_close_template_sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_definitions_accounting_close_template_versions_TemplateVersionId",
                table: "accounting_close_task_definitions",
                column: "TemplateVersionId",
                principalTable: "accounting_close_template_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_dependencies_accounting_close_tasks_DependentTaskId",
                table: "accounting_close_task_dependencies",
                column: "DependentTaskId",
                principalTable: "accounting_close_tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_dependencies_accounting_close_tasks_PredecessorTaskId",
                table: "accounting_close_task_dependencies",
                column: "PredecessorTaskId",
                principalTable: "accounting_close_tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_evidence_accounting_close_tasks_CloseTaskId",
                table: "accounting_close_task_evidence",
                column: "CloseTaskId",
                principalTable: "accounting_close_tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_task_notes_accounting_close_tasks_CloseTaskId",
                table: "accounting_close_task_notes",
                column: "CloseTaskId",
                principalTable: "accounting_close_tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_tasks_accounting_close_template_sections_SectionId",
                table: "accounting_close_tasks",
                column: "SectionId",
                principalTable: "accounting_close_template_sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_template_history_accounting_close_template_versions_TemplateVersionId",
                table: "accounting_close_template_history",
                column: "TemplateVersionId",
                principalTable: "accounting_close_template_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_template_history_accounting_close_templates_TemplateId",
                table: "accounting_close_template_history",
                column: "TemplateId",
                principalTable: "accounting_close_templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_template_sections_accounting_close_template_versions_TemplateVersionId",
                table: "accounting_close_template_sections",
                column: "TemplateVersionId",
                principalTable: "accounting_close_template_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_close_template_versions_accounting_close_templates_TemplateId",
                table: "accounting_close_template_versions",
                column: "TemplateId",
                principalTable: "accounting_close_templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_application_lines_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_application_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_evidence_links_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_evidence_links");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_template_lines_accounting_allocation_template_versions_company_id_template_version_id",
                table: "accounting_allocation_template_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_allocation_template_versions_accounting_allocation_templates_company_id_template_id",
                table: "accounting_allocation_template_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_dimension_account_policies_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_account_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_dimension_account_policies_finance_accounts_company_id_finance_account_id",
                table: "accounting_dimension_account_policies");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_dimension_members_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_members");

            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entry_line_dimensions_ledger_entry_lines_company_id_ledger_entry_line_id",
                table: "ledger_entry_line_dimensions");

            migrationBuilder.DropForeignKey(
                name: "FK_manual_journal_draft_line_dimensions_manual_journal_draft_lines_company_id_manual_journal_draft_line_id",
                table: "manual_journal_draft_line_dimensions");

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_close_templates_accounting_close_template_versions_ActiveVersionId",
                table: "accounting_close_templates");

            migrationBuilder.DropTable(
                name: "accounting_close_evidence_requirements");

            migrationBuilder.DropTable(
                name: "accounting_close_operations");

            migrationBuilder.DropTable(
                name: "accounting_close_status_history");

            migrationBuilder.DropTable(
                name: "accounting_close_task_blockers");

            migrationBuilder.DropTable(
                name: "accounting_close_task_definition_dependencies");

            migrationBuilder.DropTable(
                name: "accounting_close_task_dependencies");

            migrationBuilder.DropTable(
                name: "accounting_close_task_evidence");

            migrationBuilder.DropTable(
                name: "accounting_close_task_notes");

            migrationBuilder.DropTable(
                name: "accounting_close_template_history");

            migrationBuilder.DropTable(
                name: "accounting_close_tasks");

            migrationBuilder.DropTable(
                name: "accounting_close_instances");

            migrationBuilder.DropTable(
                name: "accounting_close_task_definitions");

            migrationBuilder.DropTable(
                name: "accounting_close_template_sections");

            migrationBuilder.DropTable(
                name: "accounting_close_template_versions");

            migrationBuilder.DropTable(
                name: "accounting_close_templates");

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_application_lines_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_application_lines",
                columns: new[] { "company_id", "application_id" },
                principalTable: "accounting_allocation_applications",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_evidence_links_accounting_allocation_applications_company_id_application_id",
                table: "accounting_allocation_evidence_links",
                columns: new[] { "company_id", "application_id" },
                principalTable: "accounting_allocation_applications",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_template_lines_accounting_allocation_template_versions_company_id_template_version_id",
                table: "accounting_allocation_template_lines",
                columns: new[] { "company_id", "template_version_id" },
                principalTable: "accounting_allocation_template_versions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_allocation_template_versions_accounting_allocation_templates_company_id_template_id",
                table: "accounting_allocation_template_versions",
                columns: new[] { "company_id", "template_id" },
                principalTable: "accounting_allocation_templates",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_dimension_account_policies_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_account_policies",
                columns: new[] { "company_id", "dimension_type_id" },
                principalTable: "accounting_dimension_types",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_dimension_account_policies_finance_accounts_company_id_finance_account_id",
                table: "accounting_dimension_account_policies",
                columns: new[] { "company_id", "finance_account_id" },
                principalTable: "finance_accounts",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_dimension_members_accounting_dimension_types_company_id_dimension_type_id",
                table: "accounting_dimension_members",
                columns: new[] { "company_id", "dimension_type_id" },
                principalTable: "accounting_dimension_types",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entry_line_dimensions_ledger_entry_lines_company_id_ledger_entry_line_id",
                table: "ledger_entry_line_dimensions",
                columns: new[] { "company_id", "ledger_entry_line_id" },
                principalTable: "ledger_entry_lines",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_manual_journal_draft_line_dimensions_manual_journal_draft_lines_company_id_manual_journal_draft_line_id",
                table: "manual_journal_draft_line_dimensions",
                columns: new[] { "company_id", "manual_journal_draft_line_id" },
                principalTable: "manual_journal_draft_lines",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
