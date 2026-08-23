using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_mapping_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mapping_type = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    scope_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    mapping_version = table.Column<int>(type: "int", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_current = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_mapping_sets", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_mapping_sets_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switch_mapping_sets_version", "[mapping_version] > 0");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_sets_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_sets_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_staged_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    extraction_batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_endpoint_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    dataset = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_record_key_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    stable_identity_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    provider_modified_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    source_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    normalized_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    normalized_data_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    financial_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    disposition = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    disposition_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    mapping_decision_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    mapping_version = table.Column<int>(type: "int", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_binding_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    duplicate_of_staged_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_current = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_staged_records", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_staged_records_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switch_staged_records_disposition", "[disposition] IN ('ready','mapped','transformed','opening_balance_representation','duplicate','excluded_with_approval','missing','unsupported','conflicting','awaiting_evidence','blocked')");
                    table.CheckConstraint("CK_accounting_provider_switch_staged_records_version", "[version] > 0");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_staged_records_accounting_provider_switch_staged_records_duplicate_of_staged_record_id",
                        column: x => x.duplicate_of_staged_record_id,
                        principalTable: "accounting_provider_switch_staged_records",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_staged_records_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_staged_records_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_staged_records_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_mapping_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mapping_set_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mapping_version = table.Column<int>(type: "int", nullable: false),
                    mapping_type = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    source_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    target_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    suggestion_method = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    is_material = table.Column<bool>(type: "bit", nullable: false),
                    affected_record_count = table.Column<long>(type: "bigint", nullable: false),
                    affected_financial_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    binding_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_mapping_decisions", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_mapping_decisions_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switch_mapping_decisions_status", "[status] IN ('suggested','awaiting_approval','approved','rejected','stale')");
                    table.CheckConstraint("CK_accounting_provider_switch_mapping_decisions_version", "[version] > 0");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_decisions_accounting_provider_switch_mapping_sets_company_id_switch_id_mapping_set_id",
                        columns: x => new { x.company_id, x.switch_id, x.mapping_set_id },
                        principalTable: "accounting_provider_switch_mapping_sets",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_decisions_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_decisions_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_decisions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_mapping_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mapping_decision_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staged_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staged_source_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    staged_normalized_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_mapping_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_records_accounting_provider_switch_mapping_decisions_company_id_switch_id_mapping_decisio~",
                        columns: x => new { x.company_id, x.switch_id, x.mapping_decision_id },
                        principalTable: "accounting_provider_switch_mapping_decisions",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_records_accounting_provider_switch_staged_records_company_id_switch_id_staged_record_id",
                        columns: x => new { x.company_id, x.switch_id, x.staged_record_id },
                        principalTable: "accounting_provider_switch_staged_records",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_mapping_records_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_decisions_approval_request_id",
                table: "accounting_provider_switch_mapping_decisions",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_decisions_company_id_mapping_set_id_source_key",
                table: "accounting_provider_switch_mapping_decisions",
                columns: new[] { "company_id", "mapping_set_id", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_decisions_company_id_switch_id_mapping_set_id",
                table: "accounting_provider_switch_mapping_decisions",
                columns: new[] { "company_id", "switch_id", "mapping_set_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_decisions_company_id_switch_id_status_mapping_type",
                table: "accounting_provider_switch_mapping_decisions",
                columns: new[] { "company_id", "switch_id", "status", "mapping_type" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_records_company_id_mapping_decision_id_staged_record_id",
                table: "accounting_provider_switch_mapping_records",
                columns: new[] { "company_id", "mapping_decision_id", "staged_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_records_company_id_switch_id_mapping_decision_id",
                table: "accounting_provider_switch_mapping_records",
                columns: new[] { "company_id", "switch_id", "mapping_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_records_company_id_switch_id_staged_record_id",
                table: "accounting_provider_switch_mapping_records",
                columns: new[] { "company_id", "switch_id", "staged_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_sets_company_id_switch_id_mapping_type_scope_key_is_current",
                table: "accounting_provider_switch_mapping_sets",
                columns: new[] { "company_id", "switch_id", "mapping_type", "scope_key", "is_current" },
                unique: true,
                filter: "[is_current] = CAST(1 AS bit)");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_mapping_sets_company_id_switch_id_mapping_type_scope_key_mapping_version",
                table: "accounting_provider_switch_mapping_sets",
                columns: new[] { "company_id", "switch_id", "mapping_type", "scope_key", "mapping_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_staged_records_approval_request_id",
                table: "accounting_provider_switch_staged_records",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_staged_records_company_id_switch_id_dataset_disposition_is_current",
                table: "accounting_provider_switch_staged_records",
                columns: new[] { "company_id", "switch_id", "dataset", "disposition", "is_current" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_staged_records_company_id_switch_id_is_current_mapping_decision_id",
                table: "accounting_provider_switch_staged_records",
                columns: new[] { "company_id", "switch_id", "is_current", "mapping_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_staged_records_company_id_switch_id_source_record_key_hash",
                table: "accounting_provider_switch_staged_records",
                columns: new[] { "company_id", "switch_id", "source_record_key_hash" },
                unique: true,
                filter: "[is_current] = CAST(1 AS bit)");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_staged_records_company_id_switch_id_stable_identity_hash",
                table: "accounting_provider_switch_staged_records",
                columns: new[] { "company_id", "switch_id", "stable_identity_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_staged_records_duplicate_of_staged_record_id",
                table: "accounting_provider_switch_staged_records",
                column: "duplicate_of_staged_record_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switch_mapping_records");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_mapping_decisions");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_staged_records");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_mapping_sets");
        }
    }
}
