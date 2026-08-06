using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal abstract class MarketingEntityConfiguration<T> : IEntityTypeConfiguration<T>
    where T : class, ICompanyOwnedEntity
{
    public abstract void Configure(EntityTypeBuilder<T> builder);
    protected static void Identity(EntityTypeBuilder<T> builder, string table)
    {
        builder.ToTable(table);
        builder.HasKey("Id");
        builder.HasAlternateKey(nameof(ICompanyOwnedEntity.CompanyId), "Id");
        builder.Property("Id").HasColumnName("id");
        builder.Property(nameof(ICompanyOwnedEntity.CompanyId)).HasColumnName("company_id").IsRequired();
        builder.HasIndex(nameof(ICompanyOwnedEntity.CompanyId));
    }
}

internal sealed class MarketingObjectiveConfiguration : MarketingEntityConfiguration<MarketingObjective>
{
    public override void Configure(EntityTypeBuilder<MarketingObjective> b)
    {
        Identity(b, "marketing_objectives");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ObjectiveType).HasColumnName("objective_type").HasMaxLength(64).IsRequired();
        b.Property(x => x.TargetValue).HasColumnName("target_value").HasPrecision(19, 4);
        b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(40).IsRequired();
        b.Property(x => x.BaselineValue).HasColumnName("baseline_value").HasPrecision(19, 4);
        b.Property(x => x.PeriodStartUtc).HasColumnName("period_start_at");
        b.Property(x => x.PeriodEndUtc).HasColumnName("period_end_at");
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.OwnerAgentId).HasColumnName("owner_agent_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.PeriodEndUtc });
    }
}

internal sealed class MarketingPlanConfiguration : MarketingEntityConfiguration<MarketingPlan>
{
    public override void Configure(EntityTypeBuilder<MarketingPlan> b)
    {
        Identity(b, "marketing_plans");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(4000).IsRequired();
        b.Property(x => x.StartsUtc).HasColumnName("starts_at");
        b.Property(x => x.EndsUtc).HasColumnName("ends_at");
        b.Property(x => x.PlannedBudget).HasColumnName("planned_budget").HasPrecision(19, 4);
        b.Property(x => x.BudgetCurrency).HasColumnName("budget_currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.OwnerAgentId).HasColumnName("owner_agent_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.StartsUtc, x.EndsUtc });
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[idempotency_key] IS NOT NULL");
    }
}

internal sealed class MarketingPlanObjectiveConfiguration : MarketingEntityConfiguration<MarketingPlanObjective>
{
    public override void Configure(EntityTypeBuilder<MarketingPlanObjective> b)
    {
        Identity(b, "marketing_plan_objectives");
        b.Property(x => x.MarketingPlanId).HasColumnName("marketing_plan_id");
        b.Property(x => x.MarketingObjectiveId).HasColumnName("marketing_objective_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingPlanId, x.MarketingObjectiveId }).IsUnique();
        b.HasOne<MarketingPlan>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingPlanId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<MarketingObjective>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingObjectiveId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MarketingContentBriefConfiguration : MarketingEntityConfiguration<MarketingContentBrief>
{
    public override void Configure(EntityTypeBuilder<MarketingContentBrief> b)
    {
        Identity(b, "marketing_content_briefs");
        b.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id");
        b.Property(x => x.MarketingPlanId).HasColumnName("marketing_plan_id");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(2000).IsRequired();
        b.Property(x => x.Audience).HasColumnName("audience").HasMaxLength(1000).IsRequired();
        b.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(40).IsRequired();
        b.Property(x => x.Language).HasColumnName("language").HasMaxLength(20).IsRequired();
        b.Property(x => x.Tone).HasColumnName("tone").HasMaxLength(120).IsRequired();
        b.Property(x => x.CallToAction).HasColumnName("call_to_action").HasMaxLength(500).IsRequired();
        b.Property(x => x.DueUtc).HasColumnName("due_at");
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.OwnerAgentId).HasColumnName("owner_agent_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.DueUtc });
    }
}

internal sealed class MarketingContentVariantConfiguration : MarketingEntityConfiguration<MarketingContentVariant>
{
    public override void Configure(EntityTypeBuilder<MarketingContentVariant> b)
    {
        Identity(b, "marketing_content_variants");
        b.Property(x => x.MarketingContentBriefId).HasColumnName("marketing_content_brief_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(x => x.Body).HasColumnName("body").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.SourceReferences).HasColumnName("source_references").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.GeneratedByAi).HasColumnName("generated_by_ai");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingContentBriefId, x.Status });
        b.HasOne<MarketingContentBrief>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingContentBriefId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MarketingSalesHandoffConfiguration : MarketingEntityConfiguration<MarketingSalesHandoff>
{
    public override void Configure(EntityTypeBuilder<MarketingSalesHandoff> b)
    {
        Identity(b, "marketing_sales_handoffs");
        b.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id");
        b.Property(x => x.ContactId).HasColumnName("contact_id");
        b.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        b.Property(x => x.LinkedLeadId).HasColumnName("linked_lead_id");
        b.Property(x => x.LinkedDealId).HasColumnName("linked_deal_id");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
        b.Property(x => x.SuggestedAction).HasColumnName("suggested_action").HasMaxLength(1000).IsRequired();
        b.Property(x => x.Urgency).HasColumnName("urgency").HasMaxLength(32).IsRequired();
        b.Property(x => x.ExpiresUtc).HasColumnName("expires_at");
        b.Property(x => x.EvidenceReferences).HasColumnName("evidence_references").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.ExpiresUtc });
    }
}

internal sealed class MarketingChannelObservationConfiguration : MarketingEntityConfiguration<MarketingChannelObservation>
{
    public override void Configure(EntityTypeBuilder<MarketingChannelObservation> b)
    {
        Identity(b, "marketing_channel_observations");
        b.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id");
        b.Property(x => x.SalesCampaignActivityId).HasColumnName("sales_campaign_activity_id");
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(80).IsRequired();
        b.Property(x => x.MetricCode).HasColumnName("metric_code").HasMaxLength(80).IsRequired();
        b.Property(x => x.Value).HasColumnName("value").HasPrecision(19, 4);
        b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(40).IsRequired();
        b.Property(x => x.PeriodStartUtc).HasColumnName("period_start_at");
        b.Property(x => x.PeriodEndUtc).HasColumnName("period_end_at");
        b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(1000).IsRequired();
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        b.Property(x => x.RetrievedUtc).HasColumnName("retrieved_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.MetricCode, x.PeriodEndUtc });
    }
}

internal sealed class MarketingExperimentConfiguration : MarketingEntityConfiguration<MarketingExperiment>
{
    public override void Configure(EntityTypeBuilder<MarketingExperiment> b)
    {
        Identity(b, "marketing_experiments");
        b.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Hypothesis).HasColumnName("hypothesis").HasMaxLength(2000).IsRequired();
        b.Property(x => x.PrimaryMetric).HasColumnName("primary_metric").HasMaxLength(80).IsRequired();
        b.Property(x => x.GuardrailMetric).HasColumnName("guardrail_metric").HasMaxLength(80).IsRequired();
        b.Property(x => x.MinimumSampleSize).HasColumnName("minimum_sample_size");
        b.Property(x => x.StartsUtc).HasColumnName("starts_at");
        b.Property(x => x.EndsUtc).HasColumnName("ends_at");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(2000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.EndsUtc });
    }
}

internal sealed class MarketingQualificationDefinitionConfiguration : MarketingEntityConfiguration<MarketingQualificationDefinition>
{
    public override void Configure(EntityTypeBuilder<MarketingQualificationDefinition> b)
    {
        Identity(b, "marketing_qualification_definitions");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.AudienceType).HasColumnName("audience_type").HasMaxLength(16).IsRequired();
        b.Property(x => x.RequiredChannel).HasColumnName("required_channel").HasMaxLength(32).IsRequired();
        b.Property(x => x.Threshold).HasColumnName("threshold").HasPrecision(5, 2);
        b.Property(x => x.FreshnessDays).HasColumnName("freshness_days");
        b.Property(x => x.RequiresCustomerCompany).HasColumnName("requires_customer_company");
        b.Property(x => x.EffectiveFromUtc).HasColumnName("effective_from_at");
        b.Property(x => x.EffectiveToUtc).HasColumnName("effective_to_at");
        b.Property(x => x.RulesJson).HasColumnName("rules_json").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.ExclusionsJson).HasColumnName("exclusions_json").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.AudienceType, x.Status, x.EffectiveFromUtc });
    }
}

internal sealed class MarketingQualificationEvaluationConfiguration : MarketingEntityConfiguration<MarketingQualificationEvaluation>
{
    public override void Configure(EntityTypeBuilder<MarketingQualificationEvaluation> b)
    {
        Identity(b, "marketing_qualification_evaluations");
        b.Property(x => x.MarketingQualificationDefinitionId).HasColumnName("marketing_qualification_definition_id");
        b.Property(x => x.DefinitionVersion).HasColumnName("definition_version");
        b.Property(x => x.ContactId).HasColumnName("contact_id");
        b.Property(x => x.Score).HasColumnName("score").HasPrecision(5, 2);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.ReasonCodesJson).HasColumnName("reason_codes_json").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.EvidenceReferencesJson).HasColumnName("evidence_references_json").HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.EvidenceObservedUtc).HasColumnName("evidence_observed_at");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        b.Property(x => x.EvaluatedUtc).HasColumnName("evaluated_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.EvaluatedUtc });
        b.HasOne<MarketingQualificationDefinition>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingQualificationDefinitionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MarketingQualificationFeedbackConfiguration : MarketingEntityConfiguration<MarketingQualificationFeedback>
{
    public override void Configure(EntityTypeBuilder<MarketingQualificationFeedback> b)
    {
        Identity(b, "marketing_qualification_feedback");
        b.Property(x => x.MarketingQualificationEvaluationId).HasColumnName("marketing_qualification_evaluation_id");
        b.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(32).IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        b.Property(x => x.LinkedLeadId).HasColumnName("linked_lead_id");
        b.Property(x => x.LinkedDealId).HasColumnName("linked_deal_id");
        b.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingQualificationEvaluationId, x.CreatedUtc });
        b.HasOne<MarketingQualificationEvaluation>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingQualificationEvaluationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
