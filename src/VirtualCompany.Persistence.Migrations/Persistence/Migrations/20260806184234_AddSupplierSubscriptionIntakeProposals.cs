using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierSubscriptionIntakeProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierSubscriptionIntakeProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEmailMessageSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEmailAttachmentSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConfidenceScore = table.Column<int>(type: "int", nullable: false),
                    EvidenceSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupplierOrgNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MatchedCounterpartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgreementName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Cadence = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BillingDay = table.Column<int>(type: "int", nullable: true),
                    StartDateUtc = table.Column<DateTime>(type: "date", nullable: true),
                    EndDateUtc = table.Column<DateTime>(type: "date", nullable: true),
                    NextExpectedBillDateUtc = table.Column<DateTime>(type: "date", nullable: true),
                    AmountTolerance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DateToleranceDays = table.Column<int>(type: "int", nullable: true),
                    NoticePeriodDays = table.Column<int>(type: "int", nullable: true),
                    AutoRenews = table.Column<bool>(type: "bit", nullable: true),
                    ContractReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SafeFailureSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AcceptedSubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierSubscriptionIntakeProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptionIntakeProposals_SupplierSubscriptions_AcceptedSubscriptionId",
                        column: x => x.AcceptedSubscriptionId,
                        principalTable: "SupplierSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptionIntakeProposals_email_attachment_snapshots_SourceEmailAttachmentSnapshotId",
                        column: x => x.SourceEmailAttachmentSnapshotId,
                        principalTable: "email_attachment_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptionIntakeProposals_email_message_snapshots_SourceEmailMessageSnapshotId",
                        column: x => x.SourceEmailMessageSnapshotId,
                        principalTable: "email_message_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptionIntakeProposals_finance_counterparties_MatchedCounterpartyId",
                        column: x => x.MatchedCounterpartyId,
                        principalTable: "finance_counterparties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierSubscriptionIntakeProposals_knowledge_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_AcceptedSubscriptionId",
                table: "SupplierSubscriptionIntakeProposals",
                column: "AcceptedSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_CompanyId_AcceptedSubscriptionId",
                table: "SupplierSubscriptionIntakeProposals",
                columns: new[] { "CompanyId", "AcceptedSubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_CompanyId_SourceEmailAttachmentSnapshotId",
                table: "SupplierSubscriptionIntakeProposals",
                columns: new[] { "CompanyId", "SourceEmailAttachmentSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_CompanyId_SourceEmailMessageSnapshotId",
                table: "SupplierSubscriptionIntakeProposals",
                columns: new[] { "CompanyId", "SourceEmailMessageSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_CompanyId_SourceFingerprint",
                table: "SupplierSubscriptionIntakeProposals",
                columns: new[] { "CompanyId", "SourceFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_CompanyId_Status_CreatedUtc",
                table: "SupplierSubscriptionIntakeProposals",
                columns: new[] { "CompanyId", "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_MatchedCounterpartyId",
                table: "SupplierSubscriptionIntakeProposals",
                column: "MatchedCounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_SourceDocumentId",
                table: "SupplierSubscriptionIntakeProposals",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_SourceEmailAttachmentSnapshotId",
                table: "SupplierSubscriptionIntakeProposals",
                column: "SourceEmailAttachmentSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionIntakeProposals_SourceEmailMessageSnapshotId",
                table: "SupplierSubscriptionIntakeProposals",
                column: "SourceEmailMessageSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierSubscriptionIntakeProposals");
        }
    }
}
