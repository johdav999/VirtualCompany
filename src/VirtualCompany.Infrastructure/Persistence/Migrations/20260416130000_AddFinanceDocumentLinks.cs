using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceDocumentLinks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "document_id",
                table: "finance_transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_id",
                table: "finance_invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_id",
                table: "finance_bills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_knowledge_documents_CompanyId_Id",
                table: "knowledge_documents",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_transactions_company_id_document_id",
                table: "finance_transactions",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_invoices_company_id_document_id",
                table: "finance_invoices",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bills_company_id_document_id",
                table: "finance_bills",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_finance_transactions_knowledge_documents_company_id_document_id",
                table: "finance_transactions",
                columns: new[] { "company_id", "document_id" },
                principalTable: "knowledge_documents",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_invoices_knowledge_documents_company_id_document_id",
                table: "finance_invoices",
                columns: new[] { "company_id", "document_id" },
                principalTable: "knowledge_documents",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_bills_knowledge_documents_company_id_document_id",
                table: "finance_bills",
                columns: new[] { "company_id", "document_id" },
                principalTable: "knowledge_documents",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_finance_transactions_knowledge_documents_company_id_document_id",
                table: "finance_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_finance_invoices_knowledge_documents_company_id_document_id",
                table: "finance_invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_finance_bills_knowledge_documents_company_id_document_id",
                table: "finance_bills");

            migrationBuilder.DropIndex(
                name: "IX_finance_transactions_company_id_document_id",
                table: "finance_transactions");

            migrationBuilder.DropIndex(
                name: "IX_finance_invoices_company_id_document_id",
                table: "finance_invoices");

            migrationBuilder.DropIndex(
                name: "IX_finance_bills_company_id_document_id",
                table: "finance_bills");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_knowledge_documents_CompanyId_Id",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "document_id",
                table: "finance_transactions");

            migrationBuilder.DropColumn(
                name: "document_id",
                table: "finance_invoices");

            migrationBuilder.DropColumn(
                name: "document_id",
                table: "finance_bills");
        }
    }
}
