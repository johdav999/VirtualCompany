using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260825213000_AddAtomicNativeCustomerInvoiceIssueR2")]
public partial class AddAtomicNativeCustomerInvoiceIssueR2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "issued_invoice_id", table: "customer_invoice_drafts", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "issued_statutory_document_id", table: "customer_invoice_drafts", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "issued_ledger_entry_id", table: "customer_invoice_drafts", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>(name: "issued_snapshot_hash", table: "customer_invoice_drafts", type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "issued_utc", table: "customer_invoice_drafts", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>(name: "authority", table: "finance_invoices", type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "imported");
        migrationBuilder.AddColumn<Guid>(name: "source_draft_id", table: "finance_invoices", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<long>(name: "source_draft_version", table: "finance_invoices", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_drafts_company_id_issued_invoice_id", table: "customer_invoice_drafts",
            columns: new[] { "company_id", "issued_invoice_id" }, unique: true, filter: "issued_invoice_id IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_finance_invoices_company_id_source_draft_id", table: "finance_invoices",
            columns: new[] { "company_id", "source_draft_id" }, unique: true, filter: "source_draft_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_customer_invoice_drafts_company_id_issued_invoice_id", table: "customer_invoice_drafts");
        migrationBuilder.DropIndex(name: "IX_finance_invoices_company_id_source_draft_id", table: "finance_invoices");
        migrationBuilder.DropColumn(name: "issued_invoice_id", table: "customer_invoice_drafts");
        migrationBuilder.DropColumn(name: "issued_statutory_document_id", table: "customer_invoice_drafts");
        migrationBuilder.DropColumn(name: "issued_ledger_entry_id", table: "customer_invoice_drafts");
        migrationBuilder.DropColumn(name: "issued_snapshot_hash", table: "customer_invoice_drafts");
        migrationBuilder.DropColumn(name: "issued_utc", table: "customer_invoice_drafts");
        migrationBuilder.DropColumn(name: "authority", table: "finance_invoices");
        migrationBuilder.DropColumn(name: "source_draft_id", table: "finance_invoices");
        migrationBuilder.DropColumn(name: "source_draft_version", table: "finance_invoices");
    }
}
