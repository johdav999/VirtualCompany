using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliverAccountingReportsCloseAndExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_export_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    media_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    content_length = table.Column<long>(type: "bigint", nullable: true),
                    content = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_export_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_export_jobs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_export_jobs_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_period_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    snapshot_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_period_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_period_history_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_period_history_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_tax_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    summary_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_tax_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_tax_reviews_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_tax_reviews_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_export_jobs_company_id_fiscal_period_id_requested_at",
                table: "accounting_export_jobs",
                columns: new[] { "company_id", "fiscal_period_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_export_jobs_company_id_idempotency_key",
                table: "accounting_export_jobs",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_export_jobs_status_next_attempt_at_requested_at",
                table: "accounting_export_jobs",
                columns: new[] { "status", "next_attempt_at", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_period_history_company_id_fiscal_period_id_occurred_at",
                table: "accounting_period_history",
                columns: new[] { "company_id", "fiscal_period_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_tax_reviews_company_id_fiscal_period_id",
                table: "accounting_tax_reviews",
                columns: new[] { "company_id", "fiscal_period_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_export_jobs");

            migrationBuilder.DropTable(
                name: "accounting_period_history");

            migrationBuilder.DropTable(
                name: "accounting_tax_reviews");
        }
    }
}
