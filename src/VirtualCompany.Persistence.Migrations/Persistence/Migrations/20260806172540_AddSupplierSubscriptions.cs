using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContractReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountTolerance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Cadence = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BillingDay = table.Column<int>(type: "int", nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "date", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "date", nullable: true),
                    NextExpectedBillDateUtc = table.Column<DateTime>(type: "date", nullable: false),
                    DateToleranceDays = table.Column<int>(type: "int", nullable: false),
                    NoticePeriodDays = table.Column<int>(type: "int", nullable: false),
                    AutoRenews = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContractDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptions_finance_counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "finance_counterparties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptions_knowledge_documents_ContractDocumentId",
                        column: x => x.ContractDocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SupplierSubscriptionBillMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "date", nullable: false),
                    ExpectedBillDateUtc = table.Column<DateTime>(type: "date", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountVariance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MatchMethod = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConfidenceScore = table.Column<int>(type: "int", nullable: false),
                    EvidenceSummary = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierSubscriptionBillMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptionBillMatches_SupplierSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "SupplierSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptionBillMatches_finance_bills_BillId",
                        column: x => x.BillId,
                        principalTable: "finance_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionBillMatches_BillId",
                table: "SupplierSubscriptionBillMatches",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionBillMatches_CompanyId_BillId",
                table: "SupplierSubscriptionBillMatches",
                columns: new[] { "CompanyId", "BillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionBillMatches_CompanyId_SubscriptionId_ExpectedBillDateUtc_Status",
                table: "SupplierSubscriptionBillMatches",
                columns: new[] { "CompanyId", "SubscriptionId", "ExpectedBillDateUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionBillMatches_SubscriptionId",
                table: "SupplierSubscriptionBillMatches",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptions_CompanyId_CounterpartyId_Name",
                table: "SupplierSubscriptions",
                columns: new[] { "CompanyId", "CounterpartyId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptions_CompanyId_Status_NextExpectedBillDateUtc",
                table: "SupplierSubscriptions",
                columns: new[] { "CompanyId", "Status", "NextExpectedBillDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptions_ContractDocumentId",
                table: "SupplierSubscriptions",
                column: "ContractDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptions_CounterpartyId",
                table: "SupplierSubscriptions",
                column: "CounterpartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierSubscriptionBillMatches");

            migrationBuilder.DropTable(
                name: "SupplierSubscriptions");
        }
    }
}
