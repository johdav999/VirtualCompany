using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260522100000_AddSupplierInvoicePaymentProposals")]
    public partial class AddSupplierInvoicePaymentProposals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_invoice_payment_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    payment_reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    audit_trail_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_invoice_payment_proposals", x => x.id);
                    table.CheckConstraint(
                        "CK_supplier_invoice_payment_proposals_status",
                        "[status] IN ('draft', 'awaiting_approval', 'ready_for_payment', 'rejected', 'cancelled', 'exported')");
                    table.ForeignKey(
                        name: "FK_supplier_invoice_payment_proposals_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_payment_proposals_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_payment_proposals_finance_bills_company_id_bill_id",
                        columns: x => new { x.company_id, x.bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_payment_proposals_finance_counterparties_company_id_supplier_id",
                        columns: x => new { x.company_id, x.supplier_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_payment_proposals_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_approval_request_id",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_bill_id",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "bill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_status_due_at",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_supplier_id",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_task_id",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_task_id",
                table: "supplier_invoice_payment_proposals",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_approval_request_id",
                table: "supplier_invoice_payment_proposals",
                column: "approval_request_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "supplier_invoice_payment_proposals");
        }
    }
}
