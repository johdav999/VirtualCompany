using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddPaymentAllocationsAndSettlementStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string3Type = isPostgres ? "character varying(3)" : "nvarchar(3)";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";

            migrationBuilder.AddColumn<string>(
                name: "settlement_status",
                table: "finance_invoices",
                type: string32Type,
                maxLength: 32,
                nullable: false,
                defaultValue: "unpaid");

            migrationBuilder.AddColumn<string>(
                name: "settlement_status",
                table: "finance_bills",
                type: string32Type,
                maxLength: 32,
                nullable: false,
                defaultValue: "unpaid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_finance_invoices_settlement_status",
                table: "finance_invoices",
                sql: "settlement_status IN ('unpaid', 'partially_paid', 'paid')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_finance_bills_settlement_status",
                table: "finance_bills",
                sql: "settlement_status IN ('unpaid', 'partially_paid', 'paid')");

            migrationBuilder.CreateIndex(
                name: "IX_finance_invoices_company_id_settlement_status_due_at",
                table: "finance_invoices",
                columns: new[] { "company_id", "settlement_status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bills_company_id_settlement_status_due_at",
                table: "finance_bills",
                columns: new[] { "company_id", "settlement_status", "due_at" });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    payment_id = table.Column<Guid>(type: guidType, nullable: false),
                    invoice_id = table.Column<Guid>(type: guidType, nullable: true),
                    bill_id = table.Column<Guid>(type: guidType, nullable: true),
                    allocated_amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_allocations", x => x.id);
                    table.CheckConstraint("CK_payment_allocations_amount_positive", "allocated_amount > 0");
                    table.CheckConstraint("CK_payment_allocations_single_target", "((invoice_id IS NOT NULL AND bill_id IS NULL) OR (invoice_id IS NULL AND bill_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_payment_allocations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_allocations_finance_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "finance_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_allocations_finance_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "finance_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_allocations_finance_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "finance_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_payment_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_invoice_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_bill_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "bill_id" });

            migrationBuilder.Sql(
                """
                UPDATE finance_invoices
                SET settlement_status = CASE
                    WHEN LOWER(status) = 'paid' THEN 'paid'
                    ELSE 'unpaid'
                END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE finance_bills
                SET settlement_status = CASE
                    WHEN LOWER(status) = 'paid' THEN 'paid'
                    ELSE 'unpaid'
                END;
                """);

            if (isPostgres)
            {
                migrationBuilder.Sql(
                    """
                    INSERT INTO payment_allocations (
                        id,
                        company_id,
                        payment_id,
                        invoice_id,
                        bill_id,
                        allocated_amount,
                        currency,
                        created_at,
                        updated_at)
                    SELECT
                        (
                            substr(md5(i.company_id::text || ':' || p.id::text || ':' || i.id::text), 1, 8) || '-' ||
                            substr(md5(i.company_id::text || ':' || p.id::text || ':' || i.id::text), 9, 4) || '-' ||
                            substr(md5(i.company_id::text || ':' || p.id::text || ':' || i.id::text), 13, 4) || '-' ||
                            substr(md5(i.company_id::text || ':' || p.id::text || ':' || i.id::text), 17, 4) || '-' ||
                            substr(md5(i.company_id::text || ':' || p.id::text || ':' || i.id::text), 21, 12)
                        )::uuid,
                        i.company_id,
                        p.id,
                        i.id,
                        NULL,
                        i.amount,
                        i.currency,
                        COALESCE(p.created_at, p.payment_date),
                        COALESCE(p.updated_at, p.payment_date)
                    FROM finance_invoices i
                    INNER JOIN finance_payments p
                        ON p.company_id = i.company_id
                        AND p.payment_type = 'incoming'
                        AND p.counterparty_reference = i.invoice_number
                        AND p.currency = i.currency
                    LEFT JOIN payment_allocations existing
                        ON existing.company_id = i.company_id
                        AND existing.invoice_id = i.id
                    WHERE LOWER(i.status) = 'paid'
                      AND existing.id IS NULL;
                    """);

                migrationBuilder.Sql(
                    """
                    INSERT INTO payment_allocations (
                        id,
                        company_id,
                        payment_id,
                        invoice_id,
                        bill_id,
                        allocated_amount,
                        currency,
                        created_at,
                        updated_at)
                    SELECT
                        (
                            substr(md5(b.company_id::text || ':' || p.id::text || ':' || b.id::text), 1, 8) || '-' ||
                            substr(md5(b.company_id::text || ':' || p.id::text || ':' || b.id::text), 9, 4) || '-' ||
                            substr(md5(b.company_id::text || ':' || p.id::text || ':' || b.id::text), 13, 4) || '-' ||
                            substr(md5(b.company_id::text || ':' || p.id::text || ':' || b.id::text), 17, 4) || '-' ||
                            substr(md5(b.company_id::text || ':' || p.id::text || ':' || b.id::text), 21, 12)
                        )::uuid,
                        b.company_id,
                        p.id,
                        NULL,
                        b.id,
                        b.amount,
                        b.currency,
                        COALESCE(p.created_at, p.payment_date),
                        COALESCE(p.updated_at, p.payment_date)
                    FROM finance_bills b
                    INNER JOIN finance_payments p
                        ON p.company_id = b.company_id
                        AND p.payment_type = 'outgoing'
                        AND p.counterparty_reference = b.bill_number
                        AND p.currency = b.currency
                    LEFT JOIN payment_allocations existing
                        ON existing.company_id = b.company_id
                        AND existing.bill_id = b.id
                    WHERE LOWER(b.status) = 'paid'
                      AND existing.id IS NULL;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    INSERT INTO payment_allocations (
                        id, company_id, payment_id, invoice_id, bill_id, allocated_amount, currency, created_at, updated_at)
                    SELECT NEWID(), i.company_id, p.id, i.id, NULL, i.amount, i.currency, COALESCE(p.created_at, p.payment_date), COALESCE(p.updated_at, p.payment_date)
                    FROM finance_invoices i
                    INNER JOIN finance_payments p
                        ON p.company_id = i.company_id
                        AND p.payment_type = 'incoming'
                        AND p.counterparty_reference = i.invoice_number
                        AND p.currency = i.currency
                    LEFT JOIN payment_allocations existing
                        ON existing.company_id = i.company_id
                        AND existing.invoice_id = i.id
                    WHERE LOWER(i.status) = 'paid'
                      AND existing.id IS NULL;
                    """);

                migrationBuilder.Sql(
                    """
                    INSERT INTO payment_allocations (
                        id, company_id, payment_id, invoice_id, bill_id, allocated_amount, currency, created_at, updated_at)
                    SELECT NEWID(), b.company_id, p.id, NULL, b.id, b.amount, b.currency, COALESCE(p.created_at, p.payment_date), COALESCE(p.updated_at, p.payment_date)
                    FROM finance_bills b
                    INNER JOIN finance_payments p
                        ON p.company_id = b.company_id
                        AND p.payment_type = 'outgoing'
                        AND p.counterparty_reference = b.bill_number
                        AND p.currency = b.currency
                    LEFT JOIN payment_allocations existing
                        ON existing.company_id = b.company_id
                        AND existing.bill_id = b.id
                    WHERE LOWER(b.status) = 'paid'
                      AND existing.id IS NULL;
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "payment_allocations");

            migrationBuilder.DropCheckConstraint(name: "CK_finance_invoices_settlement_status", table: "finance_invoices");
            migrationBuilder.DropCheckConstraint(name: "CK_finance_bills_settlement_status", table: "finance_bills");

            migrationBuilder.DropIndex(name: "IX_finance_invoices_company_id_settlement_status_due_at", table: "finance_invoices");
            migrationBuilder.DropIndex(name: "IX_finance_bills_company_id_settlement_status_due_at", table: "finance_bills");

            migrationBuilder.DropColumn(name: "settlement_status", table: "finance_invoices");
            migrationBuilder.DropColumn(name: "settlement_status", table: "finance_bills");
        }
    }
}