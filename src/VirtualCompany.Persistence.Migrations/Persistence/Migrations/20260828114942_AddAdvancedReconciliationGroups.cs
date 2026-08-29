using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedReconciliationGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_advanced_reconciliation_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    reference_normalization_pattern = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    counterparty_normalization_pattern = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    provider_pattern = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    amount_tolerance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    timing_window_days = table.Column<int>(type: "int", nullable: false),
                    recommendation_threshold = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    low_confidence_threshold = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    materiality_threshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_advanced_reconciliation_rules", x => x.id);
                    table.UniqueConstraint("AK_finance_advanced_reconciliation_rules_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_rules_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_advanced_reconciliation_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_version = table.Column<int>(type: "int", nullable: false),
                    correction_of_group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    counterparty = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    expected_bank_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    confidence_score = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    decision_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_advanced_reconciliation_groups", x => x.id);
                    table.UniqueConstraint("AK_finance_advanced_reconciliation_groups_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_advanced_reconciliation_groups_status", "status IN ('proposed', 'accepted', 'rejected', 'reversed', 'conflict')");
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_groups_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_groups_finance_advanced_reconciliation_groups_company_id_correction_of_group_id",
                        columns: x => new { x.company_id, x.correction_of_group_id },
                        principalTable: "finance_advanced_reconciliation_groups",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_groups_finance_advanced_reconciliation_rules_company_id_rule_id",
                        columns: x => new { x.company_id, x.rule_id },
                        principalTable: "finance_advanced_reconciliation_rules",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_advanced_reconciliation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_advanced_reconciliation_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_events_finance_advanced_reconciliation_groups_company_id_group_id",
                        columns: x => new { x.company_id, x.group_id },
                        principalTable: "finance_advanced_reconciliation_groups",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_advanced_reconciliation_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    node_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    direction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    adjustment_kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    debit_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    expected_record_version = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_advanced_reconciliation_nodes", x => x.id);
                    table.UniqueConstraint("AK_finance_advanced_reconciliation_nodes_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_advanced_reconciliation_nodes_type", "node_type IN ('bank_transaction', 'payment', 'invoice', 'bill', 'adjustment', 'residual')");
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_nodes_finance_advanced_reconciliation_groups_company_id_group_id",
                        columns: x => new { x.company_id, x.group_id },
                        principalTable: "finance_advanced_reconciliation_groups",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_advanced_reconciliation_reason_contributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    feature_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    contribution = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    evidence = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_advanced_reconciliation_reason_contributions", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_reason_contributions_finance_advanced_reconciliation_groups_company_id_group_id",
                        columns: x => new { x.company_id, x.group_id },
                        principalTable: "finance_advanced_reconciliation_groups",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_advanced_reconciliation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_result_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    group_version = table.Column<long>(type: "bigint", nullable: false),
                    rule_version = table.Column<int>(type: "int", nullable: false),
                    expected_bank_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    allocated_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    fee_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    rounding_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    residual_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_advanced_reconciliation_results", x => x.id);
                    table.UniqueConstraint("AK_finance_advanced_reconciliation_results_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_results_finance_advanced_reconciliation_groups_company_id_group_id",
                        columns: x => new { x.company_id, x.group_id },
                        principalTable: "finance_advanced_reconciliation_groups",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_results_finance_advanced_reconciliation_results_company_id_parent_result_id",
                        columns: x => new { x.company_id, x.parent_result_id },
                        principalTable: "finance_advanced_reconciliation_results",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "finance_advanced_reconciliation_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    edge_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_advanced_reconciliation_edges", x => x.id);
                    table.CheckConstraint("CK_finance_advanced_reconciliation_edges_type", "edge_type IN ('bank_payment', 'payment_document', 'bank_adjustment')");
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_edges_finance_advanced_reconciliation_groups_company_id_group_id",
                        columns: x => new { x.company_id, x.group_id },
                        principalTable: "finance_advanced_reconciliation_groups",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_edges_finance_advanced_reconciliation_nodes_company_id_source_node_id",
                        columns: x => new { x.company_id, x.source_node_id },
                        principalTable: "finance_advanced_reconciliation_nodes",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_advanced_reconciliation_edges_finance_advanced_reconciliation_nodes_company_id_target_node_id",
                        columns: x => new { x.company_id, x.target_node_id },
                        principalTable: "finance_advanced_reconciliation_nodes",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_edges_company_id_group_id_edge_type",
                table: "finance_advanced_reconciliation_edges",
                columns: new[] { "company_id", "group_id", "edge_type" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_edges_company_id_source_node_id_target_node_id_edge_type",
                table: "finance_advanced_reconciliation_edges",
                columns: new[] { "company_id", "source_node_id", "target_node_id", "edge_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_edges_company_id_target_node_id",
                table: "finance_advanced_reconciliation_edges",
                columns: new[] { "company_id", "target_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_events_company_id_group_id_created_at",
                table: "finance_advanced_reconciliation_events",
                columns: new[] { "company_id", "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_groups_company_id_correction_of_group_id",
                table: "finance_advanced_reconciliation_groups",
                columns: new[] { "company_id", "correction_of_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_groups_company_id_reference",
                table: "finance_advanced_reconciliation_groups",
                columns: new[] { "company_id", "reference" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_groups_company_id_rule_id",
                table: "finance_advanced_reconciliation_groups",
                columns: new[] { "company_id", "rule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_groups_company_id_rule_version",
                table: "finance_advanced_reconciliation_groups",
                columns: new[] { "company_id", "rule_version" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_groups_company_id_status_updated_at",
                table: "finance_advanced_reconciliation_groups",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_nodes_company_id_group_id_sequence",
                table: "finance_advanced_reconciliation_nodes",
                columns: new[] { "company_id", "group_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_nodes_company_id_node_type_record_id",
                table: "finance_advanced_reconciliation_nodes",
                columns: new[] { "company_id", "node_type", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_reason_contributions_company_id_group_id_feature_key",
                table: "finance_advanced_reconciliation_reason_contributions",
                columns: new[] { "company_id", "group_id", "feature_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_results_company_id_group_id_created_at",
                table: "finance_advanced_reconciliation_results",
                columns: new[] { "company_id", "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_results_company_id_parent_result_id",
                table: "finance_advanced_reconciliation_results",
                columns: new[] { "company_id", "parent_result_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_rules_company_id_superseded_at",
                table: "finance_advanced_reconciliation_rules",
                columns: new[] { "company_id", "superseded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_rules_company_id_version",
                table: "finance_advanced_reconciliation_rules",
                columns: new[] { "company_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_advanced_reconciliation_edges");

            migrationBuilder.DropTable(
                name: "finance_advanced_reconciliation_events");

            migrationBuilder.DropTable(
                name: "finance_advanced_reconciliation_reason_contributions");

            migrationBuilder.DropTable(
                name: "finance_advanced_reconciliation_results");

            migrationBuilder.DropTable(
                name: "finance_advanced_reconciliation_nodes");

            migrationBuilder.DropTable(
                name: "finance_advanced_reconciliation_groups");

            migrationBuilder.DropTable(
                name: "finance_advanced_reconciliation_rules");
        }
    }
}
