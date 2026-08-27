using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCollectionsR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_collection_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reminder_stage = table.Column<int>(type: "int", nullable: false),
                    is_on_hold = table.Column<bool>(type: "bit", nullable: false),
                    hold_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    dispute_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    dispute_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    disputed_amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: true),
                    promise_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    promise_amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: true),
                    promise_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    follow_up_due_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    work_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_collection_cases", x => x.id);
                    table.UniqueConstraint("AK_customer_collection_cases_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_collection_cases_finance_invoices_company_id_invoice_id",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "finance_invoices",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_collection_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grace_period_days = table.Column<int>(type: "int", nullable: false),
                    materiality_threshold = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    default_locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    require_approval = table.Column<bool>(type: "bit", nullable: false),
                    fees_enabled = table.Column<bool>(type: "bit", nullable: false),
                    interest_enabled = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_collection_policies", x => x.id);
                    table.UniqueConstraint("AK_customer_collection_policies_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "customer_collection_worker_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    last_failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_blocked = table.Column<bool>(type: "bit", nullable: false),
                    blocked_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_collection_worker_leases", x => x.id);
                    table.UniqueConstraint("AK_customer_collection_worker_leases_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "customer_statement_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    cutoff_date = table.Column<DateOnly>(type: "date", nullable: false),
                    time_zone_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    opening_balance = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    invoice_activity = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    allocation_activity = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    credit_activity = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    closing_balance = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_manifest_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    source_manifest_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    media_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    rendered_content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    content_length = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_statement_snapshots", x => x.id);
                    table.UniqueConstraint("AK_customer_statement_snapshots_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_statement_snapshots_finance_counterparties_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_collection_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurred_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_collection_actions", x => x.id);
                    table.UniqueConstraint("AK_customer_collection_actions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_collection_actions_customer_collection_cases_company_id_case_id",
                        columns: x => new { x.company_id, x.case_id },
                        principalTable: "customer_collection_cases",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_collection_policy_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stage = table.Column<int>(type: "int", nullable: false),
                    days_after_due = table.Column<int>(type: "int", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    template_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_collection_policy_stages", x => x.id);
                    table.UniqueConstraint("AK_customer_collection_policy_stages_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_collection_policy_stages_customer_collection_policies_company_id_policy_id",
                        columns: x => new { x.company_id, x.policy_id },
                        principalTable: "customer_collection_policies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_reminder_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    statement_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    stage = table.Column<int>(type: "int", nullable: false),
                    recipient_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    prepared_open_amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    prepared_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_reminder_drafts", x => x.id);
                    table.UniqueConstraint("AK_customer_reminder_drafts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_reminder_drafts_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_reminder_drafts_customer_collection_cases_company_id_case_id",
                        columns: x => new { x.company_id, x.case_id },
                        principalTable: "customer_collection_cases",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_reminder_drafts_customer_statement_snapshots_company_id_statement_id",
                        columns: x => new { x.company_id, x.statement_id },
                        principalTable: "customer_statement_snapshots",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_statement_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    statement_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    item_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    payment_allocation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    debit_amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    credit_amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    running_balance = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_statement_items", x => x.id);
                    table.UniqueConstraint("AK_customer_statement_items_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_statement_items_customer_statement_snapshots_company_id_statement_id",
                        columns: x => new { x.company_id, x.statement_id },
                        principalTable: "customer_statement_snapshots",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_reminder_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reminder_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    recipient_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    accepted_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_reminder_deliveries", x => x.id);
                    table.UniqueConstraint("AK_customer_reminder_deliveries_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_reminder_deliveries_customer_reminder_drafts_company_id_reminder_draft_id",
                        columns: x => new { x.company_id, x.reminder_draft_id },
                        principalTable: "customer_reminder_drafts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_actions_company_id_case_id_occurred_utc",
                table: "customer_collection_actions",
                columns: new[] { "company_id", "case_id", "occurred_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_actions_company_id_idempotency_key",
                table: "customer_collection_actions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_cases_company_id_customer_id_status",
                table: "customer_collection_cases",
                columns: new[] { "company_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_cases_company_id_follow_up_due_utc",
                table: "customer_collection_cases",
                columns: new[] { "company_id", "follow_up_due_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_cases_company_id_invoice_id",
                table: "customer_collection_cases",
                columns: new[] { "company_id", "invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_policies_company_id",
                table: "customer_collection_policies",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_policy_stages_company_id_policy_id_days_after_due",
                table: "customer_collection_policy_stages",
                columns: new[] { "company_id", "policy_id", "days_after_due" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_policy_stages_company_id_policy_id_stage",
                table: "customer_collection_policy_stages",
                columns: new[] { "company_id", "policy_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_worker_leases_company_id",
                table: "customer_collection_worker_leases",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_worker_leases_next_attempt_utc_lease_expires_utc_company_id",
                table: "customer_collection_worker_leases",
                columns: new[] { "next_attempt_utc", "lease_expires_utc", "company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_deliveries_company_id_idempotency_key",
                table: "customer_reminder_deliveries",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_deliveries_company_id_reminder_draft_id_created_utc",
                table: "customer_reminder_deliveries",
                columns: new[] { "company_id", "reminder_draft_id", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_deliveries_company_id_status_updated_utc",
                table: "customer_reminder_deliveries",
                columns: new[] { "company_id", "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_drafts_company_id_approval_request_id",
                table: "customer_reminder_drafts",
                columns: new[] { "company_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_drafts_company_id_case_id",
                table: "customer_reminder_drafts",
                columns: new[] { "company_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_drafts_company_id_idempotency_key",
                table: "customer_reminder_drafts",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_drafts_company_id_invoice_id_stage_source_hash",
                table: "customer_reminder_drafts",
                columns: new[] { "company_id", "invoice_id", "stage", "source_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_drafts_company_id_statement_id",
                table: "customer_reminder_drafts",
                columns: new[] { "company_id", "statement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_reminder_drafts_company_id_status_updated_utc",
                table: "customer_reminder_drafts",
                columns: new[] { "company_id", "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_statement_items_company_id_invoice_id",
                table: "customer_statement_items",
                columns: new[] { "company_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_statement_items_company_id_statement_id_sequence",
                table: "customer_statement_items",
                columns: new[] { "company_id", "statement_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_statement_snapshots_company_id_checksum",
                table: "customer_statement_snapshots",
                columns: new[] { "company_id", "checksum" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_statement_snapshots_company_id_customer_id_cutoff_date",
                table: "customer_statement_snapshots",
                columns: new[] { "company_id", "customer_id", "cutoff_date" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_statement_snapshots_company_id_idempotency_key",
                table: "customer_statement_snapshots",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_collection_actions");

            migrationBuilder.DropTable(
                name: "customer_collection_policy_stages");

            migrationBuilder.DropTable(
                name: "customer_collection_worker_leases");

            migrationBuilder.DropTable(
                name: "customer_reminder_deliveries");

            migrationBuilder.DropTable(
                name: "customer_statement_items");

            migrationBuilder.DropTable(
                name: "customer_collection_policies");

            migrationBuilder.DropTable(
                name: "customer_reminder_drafts");

            migrationBuilder.DropTable(
                name: "customer_collection_cases");

            migrationBuilder.DropTable(
                name: "customer_statement_snapshots");
        }
    }
}
