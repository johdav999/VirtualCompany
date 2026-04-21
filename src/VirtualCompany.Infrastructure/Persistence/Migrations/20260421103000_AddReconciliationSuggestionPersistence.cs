using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Enums;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddReconciliationSuggestionPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var decimalType = isPostgres ? "numeric(5,4)" : "decimal(5,4)";
        var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";

        migrationBuilder.CreateTable(
            name: "finance_reconciliation_suggestions",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                source_record_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                source_record_id = table.Column<Guid>(type: guidType, nullable: false),
                target_record_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                target_record_id = table.Column<Guid>(type: guidType, nullable: false),
                match_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                confidence_score = table.Column<decimal>(type: decimalType, nullable: false),
                rule_breakdown_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false, defaultValue: ReconciliationSuggestionStatuses.Open),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                created_by_user_id = table.Column<Guid>(type: guidType, nullable: false),
                updated_by_user_id = table.Column<Guid>(type: guidType, nullable: false),
                accepted_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                rejected_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                superseded_at = table.Column<DateTime>(type: dateTimeType, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_reconciliation_suggestions", x => x.id);
                table.CheckConstraint("CK_finance_reconciliation_suggestions_source_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("source_record_type"));
                table.CheckConstraint("CK_finance_reconciliation_suggestions_target_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("target_record_type"));
                table.CheckConstraint("CK_finance_reconciliation_suggestions_match_type", ReconciliationMatchTypes.BuildCheckConstraintSql("match_type"));
                table.CheckConstraint("CK_finance_reconciliation_suggestions_status", ReconciliationSuggestionStatuses.BuildCheckConstraintSql("status"));
                table.CheckConstraint("CK_finance_reconciliation_suggestions_confidence_score", "confidence_score >= 0 AND confidence_score <= 1");
                table.ForeignKey(
                    name: "FK_finance_reconciliation_suggestions_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_finance_reconciliation_suggestions_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_reconciliation_suggestions_users_updated_by_user_id",
                    column: x => x.updated_by_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "finance_reconciliation_results",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                accepted_suggestion_id = table.Column<Guid>(type: guidType, nullable: false),
                source_record_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                source_record_id = table.Column<Guid>(type: guidType, nullable: false),
                target_record_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                target_record_id = table.Column<Guid>(type: guidType, nullable: false),
                match_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                confidence_score = table.Column<decimal>(type: decimalType, nullable: false),
                rule_breakdown_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                created_by_user_id = table.Column<Guid>(type: guidType, nullable: false),
                updated_by_user_id = table.Column<Guid>(type: guidType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_reconciliation_results", x => x.id);
                table.CheckConstraint("CK_finance_reconciliation_results_source_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("source_record_type"));
                table.CheckConstraint("CK_finance_reconciliation_results_target_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("target_record_type"));
                table.CheckConstraint("CK_finance_reconciliation_results_match_type", ReconciliationMatchTypes.BuildCheckConstraintSql("match_type"));
                table.CheckConstraint("CK_finance_reconciliation_results_confidence_score", "confidence_score >= 0 AND confidence_score <= 1");
                table.ForeignKey(
                    name: "FK_finance_reconciliation_results_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_finance_reconciliation_results_finance_reconciliation_suggestions_accepted_suggestion_id",
                    column: x => x.accepted_suggestion_id,
                    principalTable: "finance_reconciliation_suggestions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_reconciliation_results_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_reconciliation_results_users_updated_by_user_id",
                    column: x => x.updated_by_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_suggestions_company_id_status_created_at",
            table: "finance_reconciliation_suggestions",
            columns: new[] { "company_id", "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_suggestions_company_id_source_record_type_source_record_id_status",
            table: "finance_reconciliation_suggestions",
            columns: new[] { "company_id", "source_record_type", "source_record_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_suggestions_company_id_target_record_type_target_record_id_status",
            table: "finance_reconciliation_suggestions",
            columns: new[] { "company_id", "target_record_type", "target_record_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_suggestions_company_id_source_record_type_source_record_id_target_record_type_target_record_id",
            table: "finance_reconciliation_suggestions",
            columns: new[] { "company_id", "source_record_type", "source_record_id", "target_record_type", "target_record_id" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_suggestions_created_by_user_id",
            table: "finance_reconciliation_suggestions",
            column: "created_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_suggestions_updated_by_user_id",
            table: "finance_reconciliation_suggestions",
            column: "updated_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_results_company_id_accepted_suggestion_id",
            table: "finance_reconciliation_results",
            columns: new[] { "company_id", "accepted_suggestion_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_results_company_id_source_record_type_source_record_id",
            table: "finance_reconciliation_results",
            columns: new[] { "company_id", "source_record_type", "source_record_id" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_results_company_id_target_record_type_target_record_id",
            table: "finance_reconciliation_results",
            columns: new[] { "company_id", "target_record_type", "target_record_id" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_results_accepted_suggestion_id",
            table: "finance_reconciliation_results",
            column: "accepted_suggestion_id");

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_results_created_by_user_id",
            table: "finance_reconciliation_results",
            column: "created_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_finance_reconciliation_results_updated_by_user_id",
            table: "finance_reconciliation_results",
            column: "updated_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "finance_reconciliation_results");

        migrationBuilder.DropTable(
            name: "finance_reconciliation_suggestions");
    }
}
