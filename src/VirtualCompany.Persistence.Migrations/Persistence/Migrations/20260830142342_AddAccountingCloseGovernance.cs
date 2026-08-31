using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingCloseGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_close_readiness_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EvidenceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    TrialBalanceChecksum = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsReady = table.Column<bool>(type: "bit", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LockedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LockedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_readiness_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_readiness_snapshots_accounting_close_instances_CloseInstanceId",
                        column: x => x.CloseInstanceId,
                        principalTable: "accounting_close_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_readiness_snapshots_finance_fiscal_periods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalTable: "finance_fiscal_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_accounting_close_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialityThreshold = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    WaiverValidityHours = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_accounting_close_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_accounting_close_policies_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_readiness_checks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsBlocking = table.Column<bool>(type: "bit", nullable: false),
                    IsWaivable = table.Column<bool>(type: "bit", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    EvidenceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ObservedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_readiness_checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_readiness_checks_accounting_close_readiness_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "accounting_close_readiness_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_reopen_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriorSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriorSnapshotHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrectionPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExecutedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_reopen_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_reopen_requests_accounting_close_instances_CloseInstanceId",
                        column: x => x.CloseInstanceId,
                        principalTable: "accounting_close_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_reopen_requests_accounting_close_readiness_snapshots_PriorSnapshotId",
                        column: x => x.PriorSnapshotId,
                        principalTable: "accounting_close_readiness_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_waivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CheckEvidenceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    EvidenceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceDocumentHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_waivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_waivers_accounting_close_instances_CloseInstanceId",
                        column: x => x.CloseInstanceId,
                        principalTable: "accounting_close_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_waivers_accounting_close_readiness_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "accounting_close_readiness_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_waivers_approval_requests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalTable: "approval_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_waivers_knowledge_documents_EvidenceDocumentId",
                        column: x => x.EvidenceDocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_sign_offs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloseInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EvidenceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_close_sign_offs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_close_sign_offs_accounting_close_instances_CloseInstanceId",
                        column: x => x.CloseInstanceId,
                        principalTable: "accounting_close_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_sign_offs_accounting_close_readiness_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "accounting_close_readiness_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_close_sign_offs_accounting_close_reopen_requests_ReopenRequestId",
                        column: x => x.ReopenRequestId,
                        principalTable: "accounting_close_reopen_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_readiness_checks_CompanyId_SnapshotId_Code",
                table: "accounting_close_readiness_checks",
                columns: new[] { "CompanyId", "SnapshotId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_readiness_checks_SnapshotId",
                table: "accounting_close_readiness_checks",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_readiness_snapshots_CloseInstanceId",
                table: "accounting_close_readiness_snapshots",
                column: "CloseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_readiness_snapshots_CompanyId_CloseInstanceId_SnapshotNumber",
                table: "accounting_close_readiness_snapshots",
                columns: new[] { "CompanyId", "CloseInstanceId", "SnapshotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_readiness_snapshots_CompanyId_CloseInstanceId_Status_UpdatedUtc",
                table: "accounting_close_readiness_snapshots",
                columns: new[] { "CompanyId", "CloseInstanceId", "Status", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_readiness_snapshots_FiscalPeriodId",
                table: "accounting_close_readiness_snapshots",
                column: "FiscalPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_reopen_requests_CloseInstanceId",
                table: "accounting_close_reopen_requests",
                column: "CloseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_reopen_requests_CompanyId_CloseInstanceId_Status_RequestedUtc",
                table: "accounting_close_reopen_requests",
                columns: new[] { "CompanyId", "CloseInstanceId", "Status", "RequestedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_reopen_requests_PriorSnapshotId",
                table: "accounting_close_reopen_requests",
                column: "PriorSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_sign_offs_CloseInstanceId",
                table: "accounting_close_sign_offs",
                column: "CloseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_sign_offs_CompanyId_CloseInstanceId_OccurredUtc",
                table: "accounting_close_sign_offs",
                columns: new[] { "CompanyId", "CloseInstanceId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_sign_offs_CompanyId_SnapshotId_Action",
                table: "accounting_close_sign_offs",
                columns: new[] { "CompanyId", "SnapshotId", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_sign_offs_ReopenRequestId",
                table: "accounting_close_sign_offs",
                column: "ReopenRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_sign_offs_SnapshotId",
                table: "accounting_close_sign_offs",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_waivers_ApprovalRequestId",
                table: "accounting_close_waivers",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_waivers_CloseInstanceId",
                table: "accounting_close_waivers",
                column: "CloseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_waivers_CompanyId_CloseInstanceId_Status_ExpiresUtc",
                table: "accounting_close_waivers",
                columns: new[] { "CompanyId", "CloseInstanceId", "Status", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_waivers_CompanyId_SnapshotId_CheckCode_CheckEvidenceHash",
                table: "accounting_close_waivers",
                columns: new[] { "CompanyId", "SnapshotId", "CheckCode", "CheckEvidenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_waivers_EvidenceDocumentId",
                table: "accounting_close_waivers",
                column: "EvidenceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_waivers_SnapshotId",
                table: "accounting_close_waivers",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_company_accounting_close_policies_CompanyId",
                table: "company_accounting_close_policies",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_close_readiness_checks");

            migrationBuilder.DropTable(
                name: "accounting_close_sign_offs");

            migrationBuilder.DropTable(
                name: "accounting_close_waivers");

            migrationBuilder.DropTable(
                name: "company_accounting_close_policies");

            migrationBuilder.DropTable(
                name: "accounting_close_reopen_requests");

            migrationBuilder.DropTable(
                name: "accounting_close_readiness_snapshots");
        }
    }
}
