using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260830240000_AddFormalYearEndRollover")]
    /// <inheritdoc />
    public sealed class AddFormalYearEndRollover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "year_end_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearStart = table.Column<DateOnly>(type: "date", nullable: false),
                    FiscalYearEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetFiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetainedEarningsAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpeningBalanceClearingAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherSeriesCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExecutedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReconciledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentReadinessSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedEvidenceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RetainedEarningsLedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpeningBalanceLedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpeningBalanceChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReconciledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_runs_finance_accounts_OpeningBalanceClearingAccountId",
                        column: x => x.OpeningBalanceClearingAccountId,
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_runs_finance_accounts_RetainedEarningsAccountId",
                        column: x => x.RetainedEarningsAccountId,
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_runs_finance_fiscal_periods_TargetFiscalPeriodId",
                        column: x => x.TargetFiscalPeriodId,
                        principalTable: "finance_fiscal_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_runs_ledger_entries_OpeningBalanceLedgerEntryId",
                        column: x => x.OpeningBalanceLedgerEntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_runs_ledger_entries_RetainedEarningsLedgerEntryId",
                        column: x => x.RetainedEarningsLedgerEntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "year_end_approval_signoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EvidenceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_approval_signoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_approval_signoffs_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "year_end_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_history_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "year_end_opening_balance_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinanceAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AccountClass = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    DimensionKey = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DimensionFactsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClosingFunctionalBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ClosingDocumentBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    OpeningFunctionalBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    OpeningDocumentBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OpeningLedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_opening_balance_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_opening_balance_candidates_finance_accounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_opening_balance_candidates_ledger_entries_OpeningLedgerEntryId",
                        column: x => x.OpeningLedgerEntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_opening_balance_candidates_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "year_end_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_operations_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "year_end_readiness_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EvidenceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JournalCutoffHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockerCount = table.Column<int>(type: "int", nullable: false),
                    ClosedPeriodCount = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_readiness_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_readiness_snapshots_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "year_end_retained_earnings_proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetainedEarningsAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpeningBalanceClearingAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NetIncome = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    EvidenceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreparedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_retained_earnings_proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_retained_earnings_proposals_finance_accounts_OpeningBalanceClearingAccountId",
                        column: x => x.OpeningBalanceClearingAccountId,
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_retained_earnings_proposals_finance_accounts_RetainedEarningsAccountId",
                        column: x => x.RetainedEarningsAccountId,
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_retained_earnings_proposals_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "year_end_subsequent_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectionLedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_subsequent_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_subsequent_events_accounting_close_reopen_requests_ReopenRequestId",
                        column: x => x.ReopenRequestId,
                        principalTable: "accounting_close_reopen_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_subsequent_events_knowledge_documents_EvidenceDocumentId",
                        column: x => x.EvidenceDocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_subsequent_events_ledger_entries_CorrectionLedgerEntryId",
                        column: x => x.CorrectionLedgerEntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_subsequent_events_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "year_end_correction_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubsequentEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectionMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_correction_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_correction_records_accounting_close_reopen_requests_ReopenRequestId",
                        column: x => x.ReopenRequestId,
                        principalTable: "accounting_close_reopen_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_correction_records_ledger_entries_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_correction_records_year_end_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "year_end_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_year_end_correction_records_year_end_subsequent_events_SubsequentEventId",
                        column: x => x.SubsequentEventId,
                        principalTable: "year_end_subsequent_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_approval_signoffs_CompanyId_RunId_OccurredUtc",
                table: "year_end_approval_signoffs",
                columns: new[] { "CompanyId", "RunId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_approval_signoffs_RunId",
                table: "year_end_approval_signoffs",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_correction_records_CompanyId_SubsequentEventId",
                table: "year_end_correction_records",
                columns: new[] { "CompanyId", "SubsequentEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_year_end_correction_records_LedgerEntryId",
                table: "year_end_correction_records",
                column: "LedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_correction_records_ReopenRequestId",
                table: "year_end_correction_records",
                column: "ReopenRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_correction_records_RunId",
                table: "year_end_correction_records",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_correction_records_SubsequentEventId",
                table: "year_end_correction_records",
                column: "SubsequentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_history_CompanyId_RunId_OccurredUtc",
                table: "year_end_history",
                columns: new[] { "CompanyId", "RunId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_history_RunId",
                table: "year_end_history",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_opening_balance_candidates_CompanyId_RunId_FinanceAccountId",
                table: "year_end_opening_balance_candidates",
                columns: new[] { "CompanyId", "RunId", "FinanceAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_opening_balance_candidates_FinanceAccountId",
                table: "year_end_opening_balance_candidates",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_opening_balance_candidates_OpeningLedgerEntryId",
                table: "year_end_opening_balance_candidates",
                column: "OpeningLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_opening_balance_candidates_RunId",
                table: "year_end_opening_balance_candidates",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_operations_CompanyId_IdempotencyKey",
                table: "year_end_operations",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_year_end_operations_RunId",
                table: "year_end_operations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_readiness_snapshots_CompanyId_RunId_SnapshotNumber",
                table: "year_end_readiness_snapshots",
                columns: new[] { "CompanyId", "RunId", "SnapshotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_year_end_readiness_snapshots_RunId",
                table: "year_end_readiness_snapshots",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_retained_earnings_proposals_CompanyId_RunId_EvidenceHash",
                table: "year_end_retained_earnings_proposals",
                columns: new[] { "CompanyId", "RunId", "EvidenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_retained_earnings_proposals_OpeningBalanceClearingAccountId",
                table: "year_end_retained_earnings_proposals",
                column: "OpeningBalanceClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_retained_earnings_proposals_RetainedEarningsAccountId",
                table: "year_end_retained_earnings_proposals",
                column: "RetainedEarningsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_retained_earnings_proposals_RunId",
                table: "year_end_retained_earnings_proposals",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_runs_CompanyId_FiscalYearStart",
                table: "year_end_runs",
                columns: new[] { "CompanyId", "FiscalYearStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_year_end_runs_CompanyId_Status_UpdatedUtc",
                table: "year_end_runs",
                columns: new[] { "CompanyId", "Status", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_runs_OpeningBalanceClearingAccountId",
                table: "year_end_runs",
                column: "OpeningBalanceClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_runs_OpeningBalanceLedgerEntryId",
                table: "year_end_runs",
                column: "OpeningBalanceLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_runs_RetainedEarningsAccountId",
                table: "year_end_runs",
                column: "RetainedEarningsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_runs_RetainedEarningsLedgerEntryId",
                table: "year_end_runs",
                column: "RetainedEarningsLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_runs_TargetFiscalPeriodId",
                table: "year_end_runs",
                column: "TargetFiscalPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_subsequent_events_CompanyId_RunId_EventDate",
                table: "year_end_subsequent_events",
                columns: new[] { "CompanyId", "RunId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_subsequent_events_CorrectionLedgerEntryId",
                table: "year_end_subsequent_events",
                column: "CorrectionLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_subsequent_events_EvidenceDocumentId",
                table: "year_end_subsequent_events",
                column: "EvidenceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_subsequent_events_ReopenRequestId",
                table: "year_end_subsequent_events",
                column: "ReopenRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_year_end_subsequent_events_RunId",
                table: "year_end_subsequent_events",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "year_end_approval_signoffs");

            migrationBuilder.DropTable(
                name: "year_end_correction_records");

            migrationBuilder.DropTable(
                name: "year_end_history");

            migrationBuilder.DropTable(
                name: "year_end_opening_balance_candidates");

            migrationBuilder.DropTable(
                name: "year_end_operations");

            migrationBuilder.DropTable(
                name: "year_end_readiness_snapshots");

            migrationBuilder.DropTable(
                name: "year_end_retained_earnings_proposals");

            migrationBuilder.DropTable(
                name: "year_end_subsequent_events");

            migrationBuilder.DropTable(
                name: "year_end_runs");
        }
    }
}
