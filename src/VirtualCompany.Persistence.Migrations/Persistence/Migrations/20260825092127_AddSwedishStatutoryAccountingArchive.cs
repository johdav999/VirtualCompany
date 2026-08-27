using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSwedishStatutoryAccountingArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "accounting_export_jobs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "encoding_name",
                table: "accounting_export_jobs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "export_type",
                table: "accounting_export_jobs",
                type: "nvarchar(48)",
                maxLength: 48,
                nullable: false,
                defaultValue: "generic_json");

            migrationBuilder.AddColumn<string>(
                name: "input_checksum",
                table: "accounting_export_jobs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lease_expires_at",
                table: "accounting_export_jobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "accounting_export_jobs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manifest_json",
                table: "accounting_export_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_account_count",
                table: "accounting_export_jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "source_credit_total",
                table: "accounting_export_jobs",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "source_debit_total",
                table: "accounting_export_jobs",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_journal_count",
                table: "accounting_export_jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_line_count",
                table: "accounting_export_jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "specification_version",
                table: "accounting_export_jobs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_key",
                table: "accounting_export_jobs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_export_jobs_company_id_fiscal_period_id_export_type_input_checksum",
                table: "accounting_export_jobs",
                columns: new[] { "company_id", "fiscal_period_id", "export_type", "input_checksum" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounting_export_jobs_company_id_fiscal_period_id_export_type_input_checksum",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "encoding_name",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "export_type",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "input_checksum",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "manifest_json",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "source_account_count",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "source_credit_total",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "source_debit_total",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "source_journal_count",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "source_line_count",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "specification_version",
                table: "accounting_export_jobs");

            migrationBuilder.DropColumn(
                name: "storage_key",
                table: "accounting_export_jobs");
        }
    }
}
