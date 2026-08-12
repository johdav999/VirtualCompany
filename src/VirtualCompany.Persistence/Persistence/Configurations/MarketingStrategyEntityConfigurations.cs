using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal static class MarketingStrategicConfiguration
{
    public static void Identity<T>(EntityTypeBuilder<T> b, string table) where T : class, ICompanyOwnedEntity
    {
        b.ToTable(table); b.HasKey("Id"); b.HasAlternateKey(nameof(ICompanyOwnedEntity.CompanyId), "Id");
        b.Property("Id").HasColumnName("id"); b.Property(nameof(ICompanyOwnedEntity.CompanyId)).HasColumnName("company_id").IsRequired();
        b.HasIndex(nameof(ICompanyOwnedEntity.CompanyId));
    }
}

internal sealed class MarketingStrategyConfiguration : IEntityTypeConfiguration<MarketingStrategy>
{
    public void Configure(EntityTypeBuilder<MarketingStrategy> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_strategies");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(4000).IsRequired();
        b.Property(x => x.BusinessContext).HasColumnName("business_context").HasMaxLength(8000).IsRequired();
        b.Property(x => x.ValidFromUtc).HasColumnName("valid_from_at"); b.Property(x => x.ValidToUtc).HasColumnName("valid_to_at");
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.SectionsJson).HasColumnName("sections_json").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.EvidenceReferencesJson).HasColumnName("evidence_references_json").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.MissingEvidenceJson).HasColumnName("missing_evidence_json").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.ValidFromUtc, x.ValidToUtc });
    }
}

internal sealed class MarketingStrategySegmentConfiguration : IEntityTypeConfiguration<MarketingStrategySegment>
{
    public void Configure(EntityTypeBuilder<MarketingStrategySegment> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_strategy_segments");
        b.Property(x => x.MarketingStrategyId).HasColumnName("marketing_strategy_id"); b.Property(x => x.MarketingCustomerSegmentId).HasColumnName("marketing_customer_segment_id");
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("marketing_customer_segment_version_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingStrategyId, x.MarketingCustomerSegmentVersionId }).IsUnique();
        b.HasOne<MarketingStrategy>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingStrategyId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<MarketingCustomerSegmentVersion>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingCustomerSegmentVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MarketingStrategyCampaignLinkConfiguration : IEntityTypeConfiguration<MarketingStrategyCampaignLink>
{
    public void Configure(EntityTypeBuilder<MarketingStrategyCampaignLink> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_strategy_campaign_links");
        b.Property(x => x.MarketingStrategyId).HasColumnName("marketing_strategy_id");
        b.Property(x => x.MarketingPlanId).HasColumnName("marketing_plan_id");
        b.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id");
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("marketing_customer_segment_version_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.MarketingStrategyId, x.SalesCampaignId }).IsUnique();
        b.HasOne<MarketingStrategy>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingStrategyId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MarketingPlan>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingPlanId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesCampaign>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SalesCampaignId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MarketingCustomerSegmentVersion>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingCustomerSegmentVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MarketingIntelligenceRecordConfiguration : IEntityTypeConfiguration<MarketingIntelligenceRecord>
{
    public void Configure(EntityTypeBuilder<MarketingIntelligenceRecord> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_intelligence_records");
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40); b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(240); b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(8000);
        b.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(24); b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(48); b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(2000);
        b.Property(x => x.ObservedUtc).HasColumnName("observed_at"); b.Property(x => x.ReviewDueUtc).HasColumnName("review_due_at");
        b.Property(x => x.DimensionsJson).HasColumnName("dimensions_json").HasColumnType("nvarchar(max)"); b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.ReviewStatus).HasColumnName("review_status").HasMaxLength(32); b.Property(x => x.IsArchived).HasColumnName("is_archived");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.Kind, x.ReviewStatus, x.ReviewDueUtc });
    }
}

internal sealed class MarketingIntelligenceReviewConfiguration : IEntityTypeConfiguration<MarketingIntelligenceReview>
{
    public void Configure(EntityTypeBuilder<MarketingIntelligenceReview> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_intelligence_reviews");
        b.Property(x => x.MarketingIntelligenceRecordId).HasColumnName("marketing_intelligence_record_id");
        b.Property(x => x.ReviewNumber).HasColumnName("review_number");
        b.Property(x => x.ReviewerUserId).HasColumnName("reviewer_user_id");
        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32);
        b.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(4000);
        b.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingIntelligenceRecordId, x.ReviewNumber }).IsUnique();
        b.HasOne<MarketingIntelligenceRecord>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingIntelligenceRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MarketingCustomerSegmentConfiguration : IEntityTypeConfiguration<MarketingCustomerSegment>
{
    public void Configure(EntityTypeBuilder<MarketingCustomerSegment> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_customer_segments"); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200); b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); b.Property(x => x.IsArchived).HasColumnName("is_archived"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
    }
}

internal sealed class MarketingCustomerSegmentVersionConfiguration : IEntityTypeConfiguration<MarketingCustomerSegmentVersion>
{
    public void Configure(EntityTypeBuilder<MarketingCustomerSegmentVersion> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_customer_segment_versions"); b.Property(x => x.MarketingCustomerSegmentId).HasColumnName("marketing_customer_segment_id"); b.Property(x => x.VersionNumber).HasColumnName("version_number");
        b.Property(x => x.CriteriaJson).HasColumnName("criteria_json").HasColumnType("nvarchar(max)"); b.Property(x => x.NeedsJson).HasColumnName("needs_json").HasColumnType("nvarchar(max)"); b.Property(x => x.BehaviorsJson).HasColumnName("behaviors_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.ChannelsJson).HasColumnName("channels_json").HasColumnType("nvarchar(max)"); b.Property(x => x.PricingJson).HasColumnName("pricing_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.SizeLow).HasColumnName("size_low"); b.Property(x => x.SizeHigh).HasColumnName("size_high"); b.Property(x => x.SizeMethod).HasColumnName("size_method").HasMaxLength(32); b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        b.Property(x => x.EconomicsJson).HasColumnName("economics_json").HasColumnType("nvarchar(max)"); b.Property(x => x.ScorecardJson).HasColumnName("scorecard_json").HasColumnType("nvarchar(max)"); b.Property(x => x.AttractivenessScore).HasColumnName("attractiveness_score").HasPrecision(5, 2);
        b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)"); b.Property(x => x.EvidenceObservedUtc).HasColumnName("evidence_observed_at"); b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.TargetState).HasColumnName("target_state").HasMaxLength(40);
        b.Property(x => x.TargetRationale).HasColumnName("target_rationale").HasMaxLength(4000); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingCustomerSegmentId, x.VersionNumber }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.TargetState });
        b.HasOne<MarketingCustomerSegment>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingCustomerSegmentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MarketingSegmentDimensionConfiguration : IEntityTypeConfiguration<MarketingSegmentDimension>
{
    public void Configure(EntityTypeBuilder<MarketingSegmentDimension> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_segment_dimensions");
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("marketing_customer_segment_version_id");
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(40);
        b.Property(x => x.Path).HasColumnName("path").HasMaxLength(500);
        b.Property(x => x.Value).HasColumnName("value").HasMaxLength(4000);
        b.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(24);
        b.Property(x => x.NumericValue).HasColumnName("numeric_value").HasPrecision(19, 4);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingCustomerSegmentVersionId, x.Category, x.Path });
        b.HasOne<MarketingCustomerSegmentVersion>().WithMany().HasForeignKey(x => new
            { x.CompanyId, x.MarketingCustomerSegmentVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MarketingOperatingRunConfiguration : IEntityTypeConfiguration<MarketingOperatingRun>
{
    public void Configure(EntityTypeBuilder<MarketingOperatingRun> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_operating_runs");
        b.Property(x => x.AgentId).HasColumnName("agent_id"); b.Property(x => x.CompanyGoalId).HasColumnName("company_goal_id"); b.Property(x => x.OperatingInitiativeId).HasColumnName("operating_initiative_id"); b.Property(x => x.WorkTaskId).HasColumnName("work_task_id");
        b.Property(x => x.TriggerType).HasColumnName("trigger_type").HasMaxLength(64); b.Property(x => x.TriggerReference).HasColumnName("trigger_reference").HasMaxLength(500); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.EffectiveAuthority).HasColumnName("effective_authority").HasMaxLength(40); b.Property(x => x.ConfigurationVersion).HasColumnName("configuration_version"); b.Property(x => x.EvidenceVersion).HasColumnName("evidence_version").HasMaxLength(100); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        b.Property(x => x.SelectedWorkJson).HasColumnName("selected_work_json").HasColumnType("nvarchar(max)"); b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)"); b.Property(x => x.MissingEvidenceJson).HasColumnName("missing_evidence_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.AssignmentContextJson).HasColumnName("assignment_context_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.OutcomeSummary).HasColumnName("outcome_summary").HasMaxLength(4000); b.Property(x => x.RecoveryCode).HasColumnName("recovery_code").HasMaxLength(100); b.Property(x => x.BudgetLimit).HasColumnName("budget_limit").HasPrecision(19, 4); b.Property(x => x.BudgetUsed).HasColumnName("budget_used").HasPrecision(19, 4);
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsConcurrencyToken(); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.AgentId, x.Status, x.CreatedUtc });
    }
}

internal sealed class MarketingOperatingActionConfiguration : IEntityTypeConfiguration<MarketingOperatingAction>
{
    public void Configure(EntityTypeBuilder<MarketingOperatingAction> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_operating_actions");
        b.Property(x => x.MarketingOperatingRunId).HasColumnName("marketing_operating_run_id");
        b.Property(x => x.Sequence).HasColumnName("sequence"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(80); b.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
        b.Property(x => x.Capability).HasColumnName("capability").HasMaxLength(120); b.Property(x => x.Tool).HasColumnName("tool").HasMaxLength(160);
        b.Property(x => x.TargetJson).HasColumnName("target_json").HasColumnType("nvarchar(max)"); b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(200);
        b.Property(x => x.GoalRelevance).HasColumnName("goal_relevance").HasMaxLength(2000); b.Property(x => x.DependenciesJson).HasColumnName("dependencies_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.ExpectedCompletionEvidence).HasColumnName("expected_completion_evidence").HasMaxLength(2000); b.Property(x => x.AuthorityDecision).HasColumnName("authority_decision").HasMaxLength(100);
        b.Property(x => x.RequiresApproval).HasColumnName("requires_approval"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(240);
        b.Property(x => x.EstimatedCost).HasColumnName("estimated_cost").HasPrecision(19, 4); b.Property(x => x.ActualCost).HasColumnName("actual_cost").HasPrecision(19, 4);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.MaximumAttempts).HasColumnName("maximum_attempts");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.ArtifactType).HasColumnName("artifact_type").HasMaxLength(100); b.Property(x => x.ArtifactId).HasColumnName("artifact_id");
        b.Property(x => x.ActualEvidenceJson).HasColumnName("actual_evidence_json").HasColumnType("nvarchar(max)"); b.Property(x => x.RecoveryCode).HasColumnName("recovery_code").HasMaxLength(100);
        b.Property(x => x.RecoveryGuidance).HasColumnName("recovery_guidance").HasMaxLength(2000); b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.HasOne<MarketingOperatingRun>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingOperatingRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.MarketingOperatingRunId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
    }
}
