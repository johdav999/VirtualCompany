using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchTargetTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_target_transfer_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_version = table.Column<int>(type: "int", nullable: false),
                    plan_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    target_provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    package_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    total_item_count = table.Column<int>(type: "int", nullable: false),
                    preview_item_count = table.Column<int>(type: "int", nullable: false),
                    preparatory_item_count = table.Column<int>(type: "int", nullable: false),
                    final_item_count = table.Column<int>(type: "int", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_target_transfer_batches", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_target_transfer_batches_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_batches_accounting_provider_switch_cutover_plans_company_id_switch_id_plan_id",
                        columns: x => new { x.company_id, x.switch_id, x.plan_id },
                        principalTable: "accounting_provider_switch_cutover_plans",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_batches_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_batches_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_target_transfer_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staged_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dataset = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    normalized_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    mapping_version = table.Column<int>(type: "int", nullable: true),
                    operation_mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    stable_identity = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    payload_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    safe_payload_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    write_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider_external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    failure_category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    reconciliation_needed = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_target_transfer_items", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_target_transfer_items_company_id_switch_id_batch_id_id", x => new { x.company_id, x.switch_id, x.batch_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_items_accounting_provider_switch_staged_records_company_id_switch_id_staged_recor~",
                        columns: x => new { x.company_id, x.switch_id, x.staged_record_id },
                        principalTable: "accounting_provider_switch_staged_records",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_items_accounting_provider_switch_target_transfer_batches_company_id_switch_id_bat~",
                        columns: x => new { x.company_id, x.switch_id, x.batch_id },
                        principalTable: "accounting_provider_switch_target_transfer_batches",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_items_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_items_fortnox_write_commands_write_request_id",
                        column: x => x.write_request_id,
                        principalTable: "fortnox_write_commands",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_target_acknowledgements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    acknowledgement_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_target_acknowledgements", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_acknowledgements_accounting_provider_switch_target_transfer_items_company_id_switch_id_bat~",
                        columns: x => new { x.company_id, x.switch_id, x.batch_id, x.item_id },
                        principalTable: "accounting_provider_switch_target_transfer_items",
                        principalColumns: new[] { "company_id", "switch_id", "batch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_target_transfer_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attempt_number = table.Column<int>(type: "int", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    failure_category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    provider_accepted_request = table.Column<bool>(type: "bit", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_target_transfer_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_target_transfer_attempts_accounting_provider_switch_target_transfer_items_company_id_switch_id_ba~",
                        columns: x => new { x.company_id, x.switch_id, x.batch_id, x.item_id },
                        principalTable: "accounting_provider_switch_target_transfer_items",
                        principalColumns: new[] { "company_id", "switch_id", "batch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_acknowledgements_company_id_item_id",
                table: "accounting_provider_switch_target_acknowledgements",
                columns: new[] { "company_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_acknowledgements_company_id_switch_id_batch_id_item_id",
                table: "accounting_provider_switch_target_acknowledgements",
                columns: new[] { "company_id", "switch_id", "batch_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_attempts_company_id_item_id_attempt_number",
                table: "accounting_provider_switch_target_transfer_attempts",
                columns: new[] { "company_id", "item_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_attempts_company_id_switch_id_batch_id_item_id",
                table: "accounting_provider_switch_target_transfer_attempts",
                columns: new[] { "company_id", "switch_id", "batch_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_batches_company_id_switch_id_idempotency_key",
                table: "accounting_provider_switch_target_transfer_batches",
                columns: new[] { "company_id", "switch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_batches_company_id_switch_id_plan_id_package_hash",
                table: "accounting_provider_switch_target_transfer_batches",
                columns: new[] { "company_id", "switch_id", "plan_id", "package_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_batches_status_next_attempt_at_lease_expires_at",
                table: "accounting_provider_switch_target_transfer_batches",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_items_approval_request_id",
                table: "accounting_provider_switch_target_transfer_items",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_items_company_id_batch_id_status",
                table: "accounting_provider_switch_target_transfer_items",
                columns: new[] { "company_id", "batch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_items_company_id_stable_identity",
                table: "accounting_provider_switch_target_transfer_items",
                columns: new[] { "company_id", "stable_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_items_company_id_switch_id_staged_record_id",
                table: "accounting_provider_switch_target_transfer_items",
                columns: new[] { "company_id", "switch_id", "staged_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_items_company_id_write_request_id",
                table: "accounting_provider_switch_target_transfer_items",
                columns: new[] { "company_id", "write_request_id" },
                unique: true,
                filter: "[write_request_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_target_transfer_items_write_request_id",
                table: "accounting_provider_switch_target_transfer_items",
                column: "write_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switch_target_acknowledgements");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_target_transfer_attempts");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_target_transfer_items");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_target_transfer_batches");
        }
    }
}
