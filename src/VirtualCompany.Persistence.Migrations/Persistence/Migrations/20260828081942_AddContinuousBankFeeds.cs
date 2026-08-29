using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuousBankFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_access_reference",
                table: "bank_discovered_accounts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bank_feed_checkpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    discovered_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_mapping_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_mapping_version = table.Column<int>(type: "int", nullable: false),
                    company_bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    stable_provider_account_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider_account_access_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    phase = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    coverage_from = table.Column<DateOnly>(type: "date", nullable: true),
                    coverage_through = table.Column<DateOnly>(type: "date", nullable: true),
                    window_from = table.Column<DateOnly>(type: "date", nullable: true),
                    window_to = table.Column<DateOnly>(type: "date", nullable: true),
                    recovery_gap_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    synchronization_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    continuation_token_envelope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    continuation_token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    page_number = table.Column<int>(type: "int", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    imported_booked_count = table.Column<int>(type: "int", nullable: false),
                    observed_pending_count = table.Column<int>(type: "int", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_successful_sync_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_feed_checkpoints", x => x.id);
                    table.UniqueConstraint("AK_bank_feed_checkpoints_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_feed_checkpoints_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_feed_checkpoints_bank_discovered_accounts_company_id_discovered_account_id",
                        columns: x => new { x.company_id, x.discovered_account_id },
                        principalTable: "bank_discovered_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_feed_checkpoints_company_bank_accounts_company_id_company_bank_account_id",
                        columns: x => new { x.company_id, x.company_bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_feed_cursor_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    checkpoint_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    synchronization_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    phase = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    cursor_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    page_number = table.Column<int>(type: "int", nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_feed_cursor_observations", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_feed_cursor_observations_bank_feed_checkpoints_company_id_checkpoint_id",
                        columns: x => new { x.company_id, x.checkpoint_id },
                        principalTable: "bank_feed_checkpoints",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_feed_gaps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    checkpoint_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    date_from = table.Column<DateOnly>(type: "date", nullable: false),
                    date_to = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    detected_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_feed_gaps", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_feed_gaps_bank_feed_checkpoints_company_id_checkpoint_id",
                        columns: x => new { x.company_id, x.checkpoint_id },
                        principalTable: "bank_feed_checkpoints",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_feed_raw_source_objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    checkpoint_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    synchronization_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    encrypted_payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    retention_expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    payload_purged_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_feed_raw_source_objects", x => x.id);
                    table.UniqueConstraint("AK_bank_feed_raw_source_objects_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_feed_raw_source_objects_bank_feed_checkpoints_company_id_checkpoint_id",
                        columns: x => new { x.company_id, x.checkpoint_id },
                        principalTable: "bank_feed_checkpoints",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_feed_balance_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    checkpoint_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    raw_source_object_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    balance_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reference_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_committed_transaction_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_feed_balance_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_feed_balance_snapshots_bank_feed_checkpoints_company_id_checkpoint_id",
                        columns: x => new { x.company_id, x.checkpoint_id },
                        principalTable: "bank_feed_checkpoints",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_feed_balance_snapshots_bank_feed_raw_source_objects_company_id_raw_source_object_id",
                        columns: x => new { x.company_id, x.raw_source_object_id },
                        principalTable: "bank_feed_raw_source_objects",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_feed_source_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    checkpoint_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stable_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    booking_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    value_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    transaction_date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    reference_text = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    counterparty = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    provider_transaction_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    raw_source_object_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    first_seen_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_feed_source_transactions", x => x.id);
                    table.UniqueConstraint("AK_bank_feed_source_transactions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_feed_source_transactions_bank_feed_checkpoints_company_id_checkpoint_id",
                        columns: x => new { x.company_id, x.checkpoint_id },
                        principalTable: "bank_feed_checkpoints",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_feed_source_transactions_bank_feed_raw_source_objects_company_id_raw_source_object_id",
                        columns: x => new { x.company_id, x.raw_source_object_id },
                        principalTable: "bank_feed_raw_source_objects",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_feed_source_transactions_bank_transactions_company_id_bank_transaction_id",
                        columns: x => new { x.company_id, x.bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_balance_snapshots_company_id_checkpoint_id_created_at",
                table: "bank_feed_balance_snapshots",
                columns: new[] { "company_id", "checkpoint_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_balance_snapshots_company_id_raw_source_object_id",
                table: "bank_feed_balance_snapshots",
                columns: new[] { "company_id", "raw_source_object_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_checkpoints_company_id_company_bank_account_id",
                table: "bank_feed_checkpoints",
                columns: new[] { "company_id", "company_bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_checkpoints_company_id_connection_id",
                table: "bank_feed_checkpoints",
                columns: new[] { "company_id", "connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_checkpoints_company_id_discovered_account_id",
                table: "bank_feed_checkpoints",
                columns: new[] { "company_id", "discovered_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_checkpoints_status_next_attempt_at_lease_expires_at",
                table: "bank_feed_checkpoints",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_cursor_observations_company_id_checkpoint_id_synchronization_run_id_phase_cursor_hash",
                table: "bank_feed_cursor_observations",
                columns: new[] { "company_id", "checkpoint_id", "synchronization_run_id", "phase", "cursor_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_gaps_company_id_checkpoint_id_status_date_from",
                table: "bank_feed_gaps",
                columns: new[] { "company_id", "checkpoint_id", "status", "date_from" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_raw_source_objects_company_id_checkpoint_id_source_identity",
                table: "bank_feed_raw_source_objects",
                columns: new[] { "company_id", "checkpoint_id", "source_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_raw_source_objects_company_id_retention_expires_at",
                table: "bank_feed_raw_source_objects",
                columns: new[] { "company_id", "retention_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_source_transactions_company_id_bank_transaction_id",
                table: "bank_feed_source_transactions",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true,
                filter: "[bank_transaction_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_source_transactions_company_id_checkpoint_id_stable_identity",
                table: "bank_feed_source_transactions",
                columns: new[] { "company_id", "checkpoint_id", "stable_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_feed_source_transactions_company_id_raw_source_object_id",
                table: "bank_feed_source_transactions",
                columns: new[] { "company_id", "raw_source_object_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_feed_balance_snapshots");

            migrationBuilder.DropTable(
                name: "bank_feed_cursor_observations");

            migrationBuilder.DropTable(
                name: "bank_feed_gaps");

            migrationBuilder.DropTable(
                name: "bank_feed_source_transactions");

            migrationBuilder.DropTable(
                name: "bank_feed_raw_source_objects");

            migrationBuilder.DropTable(
                name: "bank_feed_checkpoints");

            migrationBuilder.DropColumn(
                name: "provider_access_reference",
                table: "bank_discovered_accounts");
        }
    }
}
