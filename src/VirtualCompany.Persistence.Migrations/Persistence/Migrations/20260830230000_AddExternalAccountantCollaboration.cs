using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260830230000_AddExternalAccountantCollaboration")]
public sealed class AddExternalAccountantCollaboration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "accountant_company_grants", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), AccountantUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ScopeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false), CanViewDocuments = table.Column<bool>(type: "bit", nullable: false),
            CanRequestEvidence = table.Column<bool>(type: "bit", nullable: false), CanSignOff = table.Column<bool>(type: "bit", nullable: false),
            Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            EffectiveUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true), InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true), ApprovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true), RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            RevocationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true), LastAccessUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false), UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false), Version = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_accountant_company_grants", x => x.Id);
            table.ForeignKey("FK_accountant_company_grants_company_memberships_MembershipId", x => x.MembershipId, "company_memberships", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable(name: "accountant_review_engagements", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false), EngagementType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            AssignedAccountantUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), DueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false), UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true), Version = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_accountant_review_engagements", x => x.Id);
            table.ForeignKey("FK_accountant_review_engagements_accountant_company_grants_GrantId", x => x.GrantId, "accountant_company_grants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_accountant_review_engagements_finance_fiscal_periods_FiscalPeriodId", x => x.FiscalPeriodId, "finance_fiscal_periods", "id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable(name: "accountant_engagement_signoffs", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), EngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            SignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), Conclusion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
            ScopeSnapshot = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false), SignedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_accountant_engagement_signoffs", x => x.Id); table.ForeignKey("FK_accountant_engagement_signoffs_accountant_review_engagements_EngagementId", x => x.EngagementId, "accountant_review_engagements", "Id", onDelete: ReferentialAction.Cascade); });

        migrationBuilder.CreateTable(name: "accountant_evidence_requests", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), EngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RequestText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false), TargetType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true), DueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false), UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true), ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true), ResolutionSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_accountant_evidence_requests", x => x.Id); table.ForeignKey("FK_accountant_evidence_requests_accountant_review_engagements_EngagementId", x => x.EngagementId, "accountant_review_engagements", "Id", onDelete: ReferentialAction.Cascade); });

        migrationBuilder.CreateTable(name: "accountant_review_history", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), EngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false), TargetType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), SafeSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false), OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_accountant_review_history", x => x.Id); table.ForeignKey("FK_accountant_review_history_accountant_review_engagements_EngagementId", x => x.EngagementId, "accountant_review_engagements", "Id", onDelete: ReferentialAction.Cascade); });

        migrationBuilder.CreateTable(name: "accountant_review_items", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), EngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            IsFinding = table.Column<bool>(type: "bit", nullable: false), Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
            TargetType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true), Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
            CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false), ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true), ResolutionSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_accountant_review_items", x => x.Id); table.ForeignKey("FK_accountant_review_items_accountant_review_engagements_EngagementId", x => x.EngagementId, "accountant_review_engagements", "Id", onDelete: ReferentialAction.Cascade); });

        migrationBuilder.CreateTable(name: "accountant_evidence_responses", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ResponseText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false), RespondedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false), DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true), CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_accountant_evidence_responses", x => x.Id); table.ForeignKey("FK_accountant_evidence_responses_accountant_evidence_requests_RequestId", x => x.RequestId, "accountant_evidence_requests", "Id", onDelete: ReferentialAction.Cascade); });

        migrationBuilder.CreateIndex("IX_accountant_company_grants_AccountantUserId_Status_EffectiveFromUtc_EffectiveUntilUtc", "accountant_company_grants", new[] { "AccountantUserId", "Status", "EffectiveFromUtc", "EffectiveUntilUtc" });
        migrationBuilder.CreateIndex("IX_accountant_company_grants_CompanyId_AccountantUserId_Status", "accountant_company_grants", new[] { "CompanyId", "AccountantUserId", "Status" });
        migrationBuilder.CreateIndex("IX_accountant_company_grants_CompanyId_MembershipId", "accountant_company_grants", new[] { "CompanyId", "MembershipId" }, unique: true);
        migrationBuilder.CreateIndex("IX_accountant_company_grants_MembershipId", "accountant_company_grants", "MembershipId");
        migrationBuilder.CreateIndex("IX_accountant_review_engagements_CompanyId_AssignedAccountantUserId_Status_DueUtc", "accountant_review_engagements", new[] { "CompanyId", "AssignedAccountantUserId", "Status", "DueUtc" });
        migrationBuilder.CreateIndex("IX_accountant_review_engagements_FiscalPeriodId", "accountant_review_engagements", "FiscalPeriodId"); migrationBuilder.CreateIndex("IX_accountant_review_engagements_GrantId", "accountant_review_engagements", "GrantId");
        migrationBuilder.CreateIndex("IX_accountant_engagement_signoffs_CompanyId_EngagementId_SignedByUserId", "accountant_engagement_signoffs", new[] { "CompanyId", "EngagementId", "SignedByUserId" }, unique: true); migrationBuilder.CreateIndex("IX_accountant_engagement_signoffs_EngagementId", "accountant_engagement_signoffs", "EngagementId");
        migrationBuilder.CreateIndex("IX_accountant_evidence_requests_CompanyId_Status_DueUtc", "accountant_evidence_requests", new[] { "CompanyId", "Status", "DueUtc" }); migrationBuilder.CreateIndex("IX_accountant_evidence_requests_EngagementId", "accountant_evidence_requests", "EngagementId");
        migrationBuilder.CreateIndex("IX_accountant_evidence_responses_CompanyId_RequestId_CreatedUtc", "accountant_evidence_responses", new[] { "CompanyId", "RequestId", "CreatedUtc" }); migrationBuilder.CreateIndex("IX_accountant_evidence_responses_RequestId", "accountant_evidence_responses", "RequestId");
        migrationBuilder.CreateIndex("IX_accountant_review_history_CompanyId_EngagementId_OccurredUtc", "accountant_review_history", new[] { "CompanyId", "EngagementId", "OccurredUtc" }); migrationBuilder.CreateIndex("IX_accountant_review_history_EngagementId", "accountant_review_history", "EngagementId");
        migrationBuilder.CreateIndex("IX_accountant_review_items_CompanyId_EngagementId_Status", "accountant_review_items", new[] { "CompanyId", "EngagementId", "Status" }); migrationBuilder.CreateIndex("IX_accountant_review_items_EngagementId", "accountant_review_items", "EngagementId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("accountant_engagement_signoffs"); migrationBuilder.DropTable("accountant_evidence_responses");
        migrationBuilder.DropTable("accountant_review_history"); migrationBuilder.DropTable("accountant_review_items");
        migrationBuilder.DropTable("accountant_evidence_requests"); migrationBuilder.DropTable("accountant_review_engagements");
        migrationBuilder.DropTable("accountant_company_grants");
    }
}
