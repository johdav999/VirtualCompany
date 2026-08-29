using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurablePaymentExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_payment_batch_approval_bindings_company_id_id",
                table: "payment_batch_approval_bindings",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "payment_batch_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instruction_set_version = table.Column<int>(type: "int", nullable: false),
                    approval_binding_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    provider_payment_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    provider_authorization_uri = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    provider_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    request_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    business_idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    updates_expected = table.Column<bool>(type: "bit", nullable: false),
                    can_cancel_at_provider = table.Column<bool>(type: "bit", nullable: false),
                    status_poll_count = table.Column<int>(type: "int", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    provider_accepted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    provider_completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    settled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_executions", x => x.id);
                    table.UniqueConstraint("AK_payment_batch_executions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_payment_batch_executions_status", "status IN ('queued', 'submitting', 'awaiting_authorization', 'provider_accepted', 'processing', 'rejected', 'cancelled', 'reconciliation_required', 'provider_completed', 'settled')");
                    table.ForeignKey(
                        name: "FK_payment_batch_executions_bank_connections_company_id_bank_connection_id",
                        columns: x => new { x.company_id, x.bank_connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_batch_executions_company_bank_accounts_company_id_company_bank_account_id",
                        columns: x => new { x.company_id, x.company_bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_batch_executions_payment_batch_approval_bindings_company_id_approval_binding_id",
                        columns: x => new { x.company_id, x.approval_binding_id },
                        principalTable: "payment_batch_approval_bindings",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_batch_executions_payment_batches_company_id_batch_id",
                        columns: x => new { x.company_id, x.batch_id },
                        principalTable: "payment_batches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_reference = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    payment_count = table.Column<int>(type: "int", nullable: false),
                    allocation_count = table.Column<int>(type: "int", nullable: false),
                    ledger_entry_ids_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    settled_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    settled_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_settlements", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_batch_settlements_bank_transactions_company_id_bank_transaction_id",
                        columns: x => new { x.company_id, x.bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_batch_settlements_payment_batch_executions_company_id_execution_id",
                        columns: x => new { x.company_id, x.execution_id },
                        principalTable: "payment_batch_executions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_execution_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attempt_number = table.Column<int>(type: "int", nullable: false),
                    operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    request_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    provider_request_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    retry_classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_execution_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_execution_attempts_payment_batch_executions_company_id_execution_id",
                        columns: x => new { x.company_id, x.execution_id },
                        principalTable: "payment_batch_executions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_execution_instructions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payment_instruction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    obligation_link_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    beneficiary_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    masked_destination = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    provider_transaction_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    payment_allocation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_execution_instructions", x => x.id);
                    table.UniqueConstraint("AK_payment_execution_instructions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_payment_execution_instructions_finance_payments_company_id_payment_id",
                        columns: x => new { x.company_id, x.payment_id },
                        principalTable: "finance_payments",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_payment_execution_instructions_payment_allocations_company_id_payment_allocation_id",
                        columns: x => new { x.company_id, x.payment_allocation_id },
                        principalTable: "payment_allocations",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_payment_execution_instructions_payment_batch_executions_company_id_execution_id",
                        columns: x => new { x.company_id, x.execution_id },
                        principalTable: "payment_batch_executions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_execution_instructions_payment_batch_obligations_company_id_obligation_link_id",
                        columns: x => new { x.company_id, x.obligation_link_id },
                        principalTable: "payment_batch_obligations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_execution_instructions_payment_instructions_company_id_payment_instruction_id",
                        columns: x => new { x.company_id, x.payment_instruction_id },
                        principalTable: "payment_instructions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_provider_acknowledgements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    provider_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    normalized_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    is_final = table.Column<bool>(type: "bit", nullable: false),
                    updates_expected = table.Column<bool>(type: "bit", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_provider_acknowledgements", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_provider_acknowledgements_payment_batch_executions_company_id_execution_id",
                        columns: x => new { x.company_id, x.execution_id },
                        principalTable: "payment_batch_executions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_provider_webhook_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    webhook_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    provider_payment_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    provider_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    triggered_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_provider_webhook_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_provider_webhook_receipts_payment_batch_executions_company_id_execution_id",
                        columns: x => new { x.company_id, x.execution_id },
                        principalTable: "payment_batch_executions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_remittances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payment_instruction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    beneficiary_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    recipient_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_remittances", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_remittances_payment_batch_executions_company_id_execution_id",
                        columns: x => new { x.company_id, x.execution_id },
                        principalTable: "payment_batch_executions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_remittances_payment_instructions_company_id_payment_instruction_id",
                        columns: x => new { x.company_id, x.payment_instruction_id },
                        principalTable: "payment_instructions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_executions_company_id_approval_binding_id",
                table: "payment_batch_executions",
                columns: new[] { "company_id", "approval_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_executions_company_id_bank_connection_id",
                table: "payment_batch_executions",
                columns: new[] { "company_id", "bank_connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_executions_company_id_batch_id_instruction_set_version",
                table: "payment_batch_executions",
                columns: new[] { "company_id", "batch_id", "instruction_set_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_executions_company_id_business_idempotency_key",
                table: "payment_batch_executions",
                columns: new[] { "company_id", "business_idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_executions_company_id_company_bank_account_id",
                table: "payment_batch_executions",
                columns: new[] { "company_id", "company_bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_executions_company_id_status_updated_at",
                table: "payment_batch_executions",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_executions_provider_key_provider_payment_id",
                table: "payment_batch_executions",
                columns: new[] { "provider_key", "provider_payment_id" },
                unique: true,
                filter: "[provider_payment_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_settlements_company_id_bank_transaction_id",
                table: "payment_batch_settlements",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_settlements_company_id_execution_id",
                table: "payment_batch_settlements",
                columns: new[] { "company_id", "execution_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_attempts_company_id_execution_id_operation_attempt_number",
                table: "payment_execution_attempts",
                columns: new[] { "company_id", "execution_id", "operation", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_attempts_company_id_execution_id_outcome",
                table: "payment_execution_attempts",
                columns: new[] { "company_id", "execution_id", "outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_instructions_company_id_execution_id_payment_instruction_id",
                table: "payment_execution_instructions",
                columns: new[] { "company_id", "execution_id", "payment_instruction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_instructions_company_id_obligation_link_id",
                table: "payment_execution_instructions",
                columns: new[] { "company_id", "obligation_link_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_instructions_company_id_payment_allocation_id",
                table: "payment_execution_instructions",
                columns: new[] { "company_id", "payment_allocation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_instructions_company_id_payment_id",
                table: "payment_execution_instructions",
                columns: new[] { "company_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_instructions_company_id_payment_instruction_id",
                table: "payment_execution_instructions",
                columns: new[] { "company_id", "payment_instruction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_instructions_company_id_provider_transaction_id",
                table: "payment_execution_instructions",
                columns: new[] { "company_id", "provider_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_acknowledgements_company_id_execution_id_acknowledged_at",
                table: "payment_provider_acknowledgements",
                columns: new[] { "company_id", "execution_id", "acknowledged_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_acknowledgements_company_id_execution_id_event_identity",
                table: "payment_provider_acknowledgements",
                columns: new[] { "company_id", "execution_id", "event_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_webhook_receipts_company_id_execution_id_received_at",
                table: "payment_provider_webhook_receipts",
                columns: new[] { "company_id", "execution_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_webhook_receipts_provider_key_webhook_id",
                table: "payment_provider_webhook_receipts",
                columns: new[] { "provider_key", "webhook_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_remittances_company_id_execution_id_payment_instruction_id",
                table: "payment_remittances",
                columns: new[] { "company_id", "execution_id", "payment_instruction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_remittances_company_id_payment_instruction_id",
                table: "payment_remittances",
                columns: new[] { "company_id", "payment_instruction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_remittances_company_id_status_updated_at",
                table: "payment_remittances",
                columns: new[] { "company_id", "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_batch_settlements");

            migrationBuilder.DropTable(
                name: "payment_execution_attempts");

            migrationBuilder.DropTable(
                name: "payment_execution_instructions");

            migrationBuilder.DropTable(
                name: "payment_provider_acknowledgements");

            migrationBuilder.DropTable(
                name: "payment_provider_webhook_receipts");

            migrationBuilder.DropTable(
                name: "payment_remittances");

            migrationBuilder.DropTable(
                name: "payment_batch_executions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_payment_batch_approval_bindings_company_id_id",
                table: "payment_batch_approval_bindings");
        }
    }
}
