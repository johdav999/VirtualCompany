using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountantCompanyGrantConfiguration : IEntityTypeConfiguration<AccountantCompanyGrant>
{
    public void Configure(EntityTypeBuilder<AccountantCompanyGrant> builder)
    {
        builder.ToTable("accountant_company_grants"); builder.HasKey(x => x.Id);
        builder.Property(x => x.ScopeKey).HasMaxLength(100).IsRequired(); builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RevocationReason).HasMaxLength(1000); builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.AccountantUserId, x.Status });
        builder.HasIndex(x => new { x.AccountantUserId, x.Status, x.EffectiveFromUtc, x.EffectiveUntilUtc });
        builder.HasIndex(x => new { x.CompanyId, x.MembershipId }).IsUnique();
        builder.HasOne(x => x.Membership).WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountantReviewEngagementConfiguration : IEntityTypeConfiguration<AccountantReviewEngagement>
{
    public void Configure(EntityTypeBuilder<AccountantReviewEngagement> builder)
    {
        builder.ToTable("accountant_review_engagements"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired(); builder.Property(x => x.EngagementType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired(); builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAccountantUserId, x.Status, x.DueUtc });
        builder.HasOne(x => x.Grant).WithMany(x => x.Engagements).HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountantReviewItemConfiguration : IEntityTypeConfiguration<AccountantReviewItem>
{
    public void Configure(EntityTypeBuilder<AccountantReviewItem> builder)
    {
        builder.ToTable("accountant_review_items"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Severity).HasMaxLength(32).IsRequired(); builder.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(64).IsRequired(); builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResolutionSummary).HasMaxLength(2000); builder.HasIndex(x => new { x.CompanyId, x.EngagementId, x.Status });
        builder.HasOne(x => x.Engagement).WithMany(x => x.ReviewItems).HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountantEvidenceRequestConfiguration : IEntityTypeConfiguration<AccountantEvidenceRequest>
{
    public void Configure(EntityTypeBuilder<AccountantEvidenceRequest> builder)
    {
        builder.ToTable("accountant_evidence_requests"); builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestText).HasMaxLength(4000).IsRequired(); builder.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired(); builder.Property(x => x.ResolutionSummary).HasMaxLength(2000);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.DueUtc });
        builder.HasOne(x => x.Engagement).WithMany(x => x.EvidenceRequests).HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountantEvidenceResponseConfiguration : IEntityTypeConfiguration<AccountantEvidenceResponse>
{
    public void Configure(EntityTypeBuilder<AccountantEvidenceResponse> builder)
    {
        builder.ToTable("accountant_evidence_responses"); builder.HasKey(x => x.Id);
        builder.Property(x => x.ResponseText).HasMaxLength(4000).IsRequired(); builder.HasIndex(x => new { x.CompanyId, x.RequestId, x.CreatedUtc });
        builder.HasOne(x => x.Request).WithMany(x => x.Responses).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountantEngagementSignOffConfiguration : IEntityTypeConfiguration<AccountantEngagementSignOff>
{
    public void Configure(EntityTypeBuilder<AccountantEngagementSignOff> builder)
    {
        builder.ToTable("accountant_engagement_signoffs"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Conclusion).HasMaxLength(2000).IsRequired(); builder.Property(x => x.ScopeSnapshot).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.EngagementId, x.SignedByUserId }).IsUnique();
        builder.HasOne(x => x.Engagement).WithMany(x => x.SignOffs).HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountantReviewHistoryConfiguration : IEntityTypeConfiguration<AccountantReviewHistory>
{
    public void Configure(EntityTypeBuilder<AccountantReviewHistory> builder)
    {
        builder.ToTable("accountant_review_history"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired(); builder.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SafeSummary).HasMaxLength(2000).IsRequired(); builder.HasIndex(x => new { x.CompanyId, x.EngagementId, x.OccurredUtc });
        builder.HasOne(x => x.Engagement).WithMany(x => x.History).HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Cascade);
    }
}
