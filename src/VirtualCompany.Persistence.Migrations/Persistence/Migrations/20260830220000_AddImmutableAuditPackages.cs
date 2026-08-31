using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260830220000_AddImmutableAuditPackages")]
    /// <inheritdoc />
    public sealed class AddImmutableAuditPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScopeVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SnapshotVersionsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManifestChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PackageChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MediaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentLength = table.Column<long>(type: "bigint", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetainUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalizedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationRequested = table.Column<bool>(type: "bit", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SafeFailureSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_packages_finance_fiscal_periods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalTable: "finance_fiscal_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_package_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_package_approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_package_approvals_audit_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "audit_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_package_artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ArtifactType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DefinitionVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ContentLength = table.Column<long>(type: "bigint", nullable: true),
                    SafeDetail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_package_artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_package_artifacts_audit_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "audit_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_package_download_authorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RedeemedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_package_download_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_package_download_authorizations_audit_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "audit_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_package_generation_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SafeSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_package_generation_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_package_generation_attempts_audit_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "audit_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_package_verification_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    PackageChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ManifestChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CheckedItemCount = table.Column<int>(type: "int", nullable: false),
                    MissingItemCount = table.Column<int>(type: "int", nullable: false),
                    CorruptItemCount = table.Column<int>(type: "int", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SafeSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_package_verification_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_package_verification_results_audit_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "audit_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_approvals_CompanyId_PackageId_DecidedUtc",
                table: "audit_package_approvals",
                columns: new[] { "CompanyId", "PackageId", "DecidedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_approvals_PackageId",
                table: "audit_package_approvals",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_artifacts_CompanyId_PackageId_Sequence",
                table: "audit_package_artifacts",
                columns: new[] { "CompanyId", "PackageId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_artifacts_CompanyId_PackageId_Status",
                table: "audit_package_artifacts",
                columns: new[] { "CompanyId", "PackageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_artifacts_PackageId",
                table: "audit_package_artifacts",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_download_authorizations_CompanyId_PackageId_UserId_ExpiresUtc",
                table: "audit_package_download_authorizations",
                columns: new[] { "CompanyId", "PackageId", "UserId", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_download_authorizations_CompanyId_TokenHash",
                table: "audit_package_download_authorizations",
                columns: new[] { "CompanyId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_download_authorizations_PackageId",
                table: "audit_package_download_authorizations",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_generation_attempts_CompanyId_PackageId_AttemptNumber",
                table: "audit_package_generation_attempts",
                columns: new[] { "CompanyId", "PackageId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_generation_attempts_PackageId",
                table: "audit_package_generation_attempts",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_verification_results_CompanyId_PackageId_VerifiedUtc",
                table: "audit_package_verification_results",
                columns: new[] { "CompanyId", "PackageId", "VerifiedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_package_verification_results_PackageId",
                table: "audit_package_verification_results",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_packages_CompanyId_FiscalPeriodId_ScopeKey_ScopeVersion_ScopeHash",
                table: "audit_packages",
                columns: new[] { "CompanyId", "FiscalPeriodId", "ScopeKey", "ScopeVersion", "ScopeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_packages_CompanyId_IdempotencyKey",
                table: "audit_packages",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_packages_CompanyId_RetainUntilUtc",
                table: "audit_packages",
                columns: new[] { "CompanyId", "RetainUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_packages_FiscalPeriodId",
                table: "audit_packages",
                column: "FiscalPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_packages_Status_NextAttemptUtc",
                table: "audit_packages",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_packages_Status_LeaseExpiresUtc",
                table: "audit_packages",
                columns: new[] { "Status", "LeaseExpiresUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_package_approvals");

            migrationBuilder.DropTable(
                name: "audit_package_artifacts");

            migrationBuilder.DropTable(
                name: "audit_package_download_authorizations");

            migrationBuilder.DropTable(
                name: "audit_package_generation_attempts");

            migrationBuilder.DropTable(
                name: "audit_package_verification_results");

            migrationBuilder.DropTable(
                name: "audit_packages");
        }
    }
}
