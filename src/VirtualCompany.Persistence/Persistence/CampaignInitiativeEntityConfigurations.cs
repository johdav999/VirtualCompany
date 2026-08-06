using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal abstract class CampaignEntityConfiguration<T> : IEntityTypeConfiguration<T>
    where T : class, ICompanyOwnedEntity
{
    public abstract void Configure(EntityTypeBuilder<T> builder);

    protected static void ConfigureIdentity(EntityTypeBuilder<T> builder, string table)
    {
        builder.ToTable(table);
        builder.HasKey("Id");
        builder.HasAlternateKey(nameof(ICompanyOwnedEntity.CompanyId), "Id");
        builder.Property("Id").HasColumnName("id");
        builder.Property(nameof(ICompanyOwnedEntity.CompanyId)).HasColumnName("company_id").IsRequired();
        builder.HasIndex(nameof(ICompanyOwnedEntity.CompanyId));
    }
}

internal sealed class SalesCampaignObjectiveConfiguration : CampaignEntityConfiguration<SalesCampaignObjective>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignObjective> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_objectives");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.ObjectiveType).HasColumnName("objective_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetValue).HasColumnName("target_value").HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(40).IsRequired();
        builder.Property(x => x.TargetUtc).HasColumnName("target_at").IsRequired();
        builder.Property(x => x.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.IsPrimary });
        CampaignRelationship(builder, x => x.SalesCampaign, x => x.Objectives, DeleteBehavior.Cascade);
    }

    private static void CampaignRelationship(
        EntityTypeBuilder<SalesCampaignObjective> builder,
        System.Linq.Expressions.Expression<Func<SalesCampaignObjective, SalesCampaign>> navigation,
        System.Linq.Expressions.Expression<Func<SalesCampaign, IEnumerable<SalesCampaignObjective>?>> inverse,
        DeleteBehavior deleteBehavior) =>
        builder.HasOne(navigation).WithMany(inverse)
            .HasForeignKey(nameof(SalesCampaignObjective.CompanyId), nameof(SalesCampaignObjective.SalesCampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(deleteBehavior);
}

internal sealed class SalesCampaignOfferConfiguration : CampaignEntityConfiguration<SalesCampaignOffer>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignOffer> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_offers");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(40).IsRequired();
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(512).IsRequired();
        builder.Property(x => x.KnowledgeDocumentId).HasColumnName("knowledge_document_id");
        builder.Property(x => x.NoOfferRequired).HasColumnName("no_offer_required").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId });
        builder.HasOne(x => x.SalesCampaign).WithMany(x => x.Offers)
            .HasForeignKey(nameof(SalesCampaignOffer.CompanyId), nameof(SalesCampaignOffer.SalesCampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesCampaignAudienceSegmentConfiguration : CampaignEntityConfiguration<SalesCampaignAudienceSegment>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignAudienceSegment> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_audience_segments");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.SegmentKind).HasColumnName("segment_kind").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Industry).HasColumnName("industry").HasMaxLength(120);
        builder.Property(x => x.Country).HasColumnName("country").HasMaxLength(80);
        builder.Property(x => x.MinEmployees).HasColumnName("min_employees");
        builder.Property(x => x.MaxEmployees).HasColumnName("max_employees");
        builder.Property(x => x.BuyingRole).HasColumnName("buying_role").HasMaxLength(80);
        builder.Property(x => x.CustomerLifecycle).HasColumnName("customer_lifecycle").HasMaxLength(80);
        builder.Property(x => x.ProductInterest).HasColumnName("product_interest").HasMaxLength(160);
        builder.Property(x => x.PreferredLanguage).HasColumnName("preferred_language").HasMaxLength(20);
        builder.Property(x => x.RequireCommunicationPermission).HasColumnName("require_communication_permission").IsRequired();
        builder.Property(x => x.ExcludeOpenCriticalSupportCases).HasColumnName("exclude_open_critical_support_cases").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IsActive, x.SegmentKind });
    }
}

internal sealed class SalesCampaignAudienceSnapshotConfiguration : CampaignEntityConfiguration<SalesCampaignAudienceSnapshot>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignAudienceSnapshot> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_audience_snapshots");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.AudienceSegmentId).HasColumnName("audience_segment_id");
        builder.Property(x => x.SegmentVersion).HasColumnName("segment_version").IsRequired();
        builder.Property(x => x.SnapshotVersion).HasColumnName("snapshot_version").IsRequired();
        builder.Property(x => x.CapturedUtc).HasColumnName("captured_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.SnapshotVersion }).IsUnique();
        builder.HasOne<SalesCampaign>().WithMany()
            .HasForeignKey(nameof(SalesCampaignAudienceSnapshot.CompanyId), nameof(SalesCampaignAudienceSnapshot.SalesCampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesCampaignAudienceMemberConfiguration : CampaignEntityConfiguration<SalesCampaignAudienceMember>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignAudienceMember> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_audience_members");
        builder.Property(x => x.AudienceSnapshotId).HasColumnName("audience_snapshot_id").IsRequired();
        builder.Property(x => x.ContactId).HasColumnName("contact_id");
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        builder.Property(x => x.ProspectAccountId).HasColumnName("prospect_account_id");
        builder.Property(x => x.EligibilityStatus).HasColumnName("eligibility_status").HasMaxLength(40).IsRequired();
        builder.Property(x => x.InclusionReason).HasColumnName("inclusion_reason").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ConsentStatus).HasColumnName("consent_status").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CommunicationLanguage).HasColumnName("communication_language").HasMaxLength(20);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.AudienceSnapshotId, x.ContactId });
        builder.HasOne<SalesCampaignAudienceSnapshot>().WithMany(x => x.Members)
            .HasForeignKey(nameof(SalesCampaignAudienceMember.CompanyId), nameof(SalesCampaignAudienceMember.AudienceSnapshotId))
            .HasPrincipalKey(nameof(SalesCampaignAudienceSnapshot.CompanyId), nameof(SalesCampaignAudienceSnapshot.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesCampaignMilestoneConfiguration : CampaignEntityConfiguration<SalesCampaignMilestone>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignMilestone> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_milestones");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.DueUtc).HasColumnName("due_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.DueUtc });
        builder.HasOne<SalesCampaign>().WithMany()
            .HasForeignKey(nameof(SalesCampaignMilestone.CompanyId), nameof(SalesCampaignMilestone.SalesCampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesCampaignActivityConfiguration : CampaignEntityConfiguration<SalesCampaignActivity>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignActivity> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_activities");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.MilestoneId).HasColumnName("milestone_id");
        builder.Property(x => x.DependsOnActivityId).HasColumnName("depends_on_activity_id");
        builder.Property(x => x.SalesSequenceStepId).HasColumnName("sales_sequence_step_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActivityType).HasColumnName("activity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(40).IsRequired();
        builder.Property(x => x.ExecutionMode).HasColumnName("execution_mode").HasMaxLength(40).IsRequired();
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(x => x.OwnerAgentId).HasColumnName("owner_agent_id");
        builder.Property(x => x.PlannedStartUtc).HasColumnName("planned_start_at").IsRequired();
        builder.Property(x => x.DueUtc).HasColumnName("due_at").IsRequired();
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.RequiredToolCapability).HasColumnName("required_tool_capability").HasMaxLength(120);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        builder.Property(x => x.ResultSummary).HasColumnName("result_summary").HasMaxLength(1000);
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(1000);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.ClaimedUtc).HasColumnName("claimed_at");
        builder.Property(x => x.ClaimToken).HasColumnName("claim_token").HasMaxLength(64);
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.PlannedStartUtc });
        builder.HasOne(x => x.SalesCampaign).WithMany(x => x.Activities)
            .HasForeignKey(nameof(SalesCampaignActivity.CompanyId), nameof(SalesCampaignActivity.SalesCampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesCampaignKpiDefinitionConfiguration : CampaignEntityConfiguration<SalesCampaignKpiDefinition>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignKpiDefinition> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_kpi_definitions");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Numerator).HasColumnName("numerator").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Denominator).HasColumnName("denominator").HasMaxLength(80);
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Baseline).HasColumnName("baseline").HasPrecision(19, 4);
        builder.Property(x => x.Target).HasColumnName("target").HasPrecision(19, 4);
        builder.Property(x => x.AttributionWindowDays).HasColumnName("attribution_window_days").IsRequired();
        builder.Property(x => x.DataSource).HasColumnName("data_source").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.Key, x.Version }).IsUnique();
    }
}

internal sealed class SalesCampaignKpiSnapshotConfiguration : CampaignEntityConfiguration<SalesCampaignKpiSnapshot>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignKpiSnapshot> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_kpi_snapshots");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.DefinitionId).HasColumnName("definition_id").IsRequired();
        builder.Property(x => x.DefinitionVersion).HasColumnName("definition_version").IsRequired();
        builder.Property(x => x.NumeratorValue).HasColumnName("numerator_value").HasPrecision(19, 4);
        builder.Property(x => x.DenominatorValue).HasColumnName("denominator_value").HasPrecision(19, 4);
        builder.Property(x => x.MetricValue).HasColumnName("metric_value").HasPrecision(19, 4);
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.EvidenceSummary).HasColumnName("evidence_summary").HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.DefinitionId, x.ObservedUtc });
    }
}

internal sealed class SalesCampaignCostConfiguration : CampaignEntityConfiguration<SalesCampaignCost>
{
    public override void Configure(EntityTypeBuilder<SalesCampaignCost> builder)
    {
        ConfigureIdentity(builder, "sales_campaign_costs");
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.FinanceRecordId).HasColumnName("finance_record_id");
        builder.Property(x => x.SalesCampaignActivityId).HasColumnName("sales_campaign_activity_id");
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.Classification, x.Currency, x.ObservedUtc });
    }
}
