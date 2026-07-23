using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260514103000_AddNormalizedFinanceDocumentStatuses")]
    public partial class AddNormalizedFinanceDocumentStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_finance_invoices_settlement_status",
                table: "finance_invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_finance_bills_settlement_status",
                table: "finance_bills");

            AddDocumentStatusColumns(
                migrationBuilder,
                "finance_invoices",
                "invoice");

            AddDocumentStatusColumns(
                migrationBuilder,
                "finance_bills",
                "supplier_invoice");

            BackfillDocumentStatuses(migrationBuilder, "finance_invoices", "invoice", "credit_note");
            BackfillDocumentStatuses(migrationBuilder, "finance_bills", "supplier_invoice", "supplier_credit_note");

            AddDocumentStatusConstraints(migrationBuilder, "finance_invoices");
            AddDocumentStatusConstraints(migrationBuilder, "finance_bills");

            migrationBuilder.CreateIndex(
                name: "IX_finance_invoices_company_id_posting_status_settlement_status_due_at",
                table: "finance_invoices",
                columns: new[] { "company_id", "posting_status", "settlement_status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_invoices_company_id_document_kind_due_at",
                table: "finance_invoices",
                columns: new[] { "company_id", "document_kind", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bills_company_id_posting_status_settlement_status_due_at",
                table: "finance_bills",
                columns: new[] { "company_id", "posting_status", "settlement_status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bills_company_id_document_kind_due_at",
                table: "finance_bills",
                columns: new[] { "company_id", "document_kind", "due_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_finance_invoices_company_id_posting_status_settlement_status_due_at",
                table: "finance_invoices");

            migrationBuilder.DropIndex(
                name: "IX_finance_invoices_company_id_document_kind_due_at",
                table: "finance_invoices");

            migrationBuilder.DropIndex(
                name: "IX_finance_bills_company_id_posting_status_settlement_status_due_at",
                table: "finance_bills");

            migrationBuilder.DropIndex(
                name: "IX_finance_bills_company_id_document_kind_due_at",
                table: "finance_bills");

            DropDocumentStatusConstraints(migrationBuilder, "finance_invoices");
            DropDocumentStatusConstraints(migrationBuilder, "finance_bills");

            DropDocumentStatusColumns(migrationBuilder, "finance_invoices");
            DropDocumentStatusColumns(migrationBuilder, "finance_bills");

            migrationBuilder.AddCheckConstraint(
                name: "CK_finance_invoices_settlement_status",
                table: "finance_invoices",
                sql: "settlement_status IN ('unpaid', 'partially_paid', 'paid')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_finance_bills_settlement_status",
                table: "finance_bills",
                sql: "settlement_status IN ('unpaid', 'partially_paid', 'paid')");
        }

        private static void AddDocumentStatusColumns(
            MigrationBuilder migrationBuilder,
            string table,
            string defaultDocumentKind)
        {
            migrationBuilder.AddColumn<string>(
                name: "posting_status",
                table: table,
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "booked");

            migrationBuilder.AddColumn<string>(
                name: "due_status",
                table: table,
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "not_due");

            migrationBuilder.AddColumn<string>(
                name: "document_kind",
                table: table,
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: defaultDocumentKind);

            migrationBuilder.AddColumn<string>(
                name: "provider_status",
                table: table,
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        private static void DropDocumentStatusColumns(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.DropColumn(name: "posting_status", table: table);
            migrationBuilder.DropColumn(name: "due_status", table: table);
            migrationBuilder.DropColumn(name: "document_kind", table: table);
            migrationBuilder.DropColumn(name: "provider_status", table: table);
        }

        private static void BackfillDocumentStatuses(
            MigrationBuilder migrationBuilder,
            string table,
            string defaultDocumentKind,
            string creditDocumentKind)
        {
            migrationBuilder.Sql($@"
                UPDATE {table}
                SET
                    posting_status = CASE
                        WHEN LOWER(status) IN ('draft', 'unbooked', 'pending', 'pending_approval') THEN 'draft'
                        WHEN LOWER(status) IN ('cancelled', 'canceled', 'void', 'rejected') THEN 'cancelled'
                        ELSE 'booked'
                    END,
                    settlement_status = CASE
                        WHEN LOWER(status) IN ('credited') THEN 'credited'
                        WHEN LOWER(status) IN ('paid') THEN 'paid'
                        ELSE settlement_status
                    END,
                    due_status = CASE
                        WHEN LOWER(status) IN ('paid', 'credited') OR settlement_status IN ('paid', 'credited') THEN 'not_due'
                        WHEN due_at < SYSUTCDATETIME() THEN 'overdue'
                        WHEN due_at <= DATEADD(day, 7, SYSUTCDATETIME()) THEN 'due_soon'
                        ELSE 'not_due'
                    END,
                    document_kind = CASE
                        WHEN amount < 0 THEN '{creditDocumentKind}'
                        ELSE '{defaultDocumentKind}'
                    END,
                    provider_status = NULL
                ");
        }

        private static void AddDocumentStatusConstraints(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddCheckConstraint(
                name: $"CK_{table}_posting_status",
                table: table,
                sql: "posting_status IN ('draft', 'booked', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: $"CK_{table}_settlement_status",
                table: table,
                sql: "settlement_status IN ('unpaid', 'partially_paid', 'paid', 'credited')");

            migrationBuilder.AddCheckConstraint(
                name: $"CK_{table}_due_status",
                table: table,
                sql: "due_status IN ('not_due', 'due_soon', 'overdue')");

            migrationBuilder.AddCheckConstraint(
                name: $"CK_{table}_document_kind",
                table: table,
                sql: "document_kind IN ('invoice', 'credit_note', 'supplier_invoice', 'supplier_credit_note')");
        }

        private static void DropDocumentStatusConstraints(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.DropCheckConstraint(name: $"CK_{table}_posting_status", table: table);
            migrationBuilder.DropCheckConstraint(name: $"CK_{table}_settlement_status", table: table);
            migrationBuilder.DropCheckConstraint(name: $"CK_{table}_due_status", table: table);
            migrationBuilder.DropCheckConstraint(name: $"CK_{table}_document_kind", table: table);
        }
    }
}
