using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesCampaignPresentationActivityConfiguration : IEntityTypeConfiguration<SalesCampaignPresentationActivity>
{
    public void Configure(EntityTypeBuilder<SalesCampaignPresentationActivity> b)
    {
        b.ToTable("sales_campaign_presentation_activities", t => t.HasCheckConstraint("CK_sales_campaign_presentation_lead_time", "preparation_lead_time_hours BETWEEN 0 AND 2160"));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SalesCampaignActivityId).HasColumnName("sales_campaign_activity_id"); b.Property(x => x.PresetVersionId).HasColumnName("preset_version_id");
        b.Property(x => x.ExecutionScope).HasColumnName("execution_scope").HasConversion(x => x.ToValue(), x => SalesCampaignPresentationValues.ParseScope(x)).HasMaxLength(32);
        b.Property(x => x.PresenterStrategy).HasColumnName("presenter_strategy").HasConversion(x => x.ToValue(), x => SalesCampaignPresentationValues.ParsePresenterStrategy(x)).HasMaxLength(32);
        b.Property(x => x.ExplicitPresenterAgentId).HasColumnName("explicit_presenter_agent_id");
        b.Property(x => x.WorkStrategy).HasColumnName("work_strategy").HasConversion(x => x.ToValue(), x => SalesCampaignPresentationValues.ParseWorkStrategy(x)).HasMaxLength(32);
        b.Property(x => x.AllowOverrides).HasColumnName("allow_overrides"); b.Property(x => x.EventSessionId).HasColumnName("event_session_id");
        b.Property(x => x.PreparationLeadTimeHours).HasColumnName("preparation_lead_time_hours"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.SalesCampaignActivityId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.PresetVersionId });
        b.HasOne(x => x.Activity).WithOne().HasForeignKey<SalesCampaignPresentationActivity>(x => new { x.CompanyId, x.SalesCampaignActivityId }).HasPrincipalKey<SalesCampaignActivity>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PresetVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.PresetVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ExplicitPresenterAgent).WithMany().HasForeignKey(x => new { x.CompanyId, x.ExplicitPresenterAgentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EventSession).WithMany().HasForeignKey(x => new { x.CompanyId, x.EventSessionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesCampaignPresentationRunConfiguration : IEntityTypeConfiguration<SalesCampaignPresentationRun>
{
    public void Configure(EntityTypeBuilder<SalesCampaignPresentationRun> b)
    {
        b.ToTable("sales_campaign_presentation_runs"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.ConfigurationId).HasColumnName("configuration_id"); b.Property(x => x.PresentationRunId).HasColumnName("presentation_run_id");
        b.Property(x => x.SubjectType).HasColumnName("subject_type").HasMaxLength(32); b.Property(x => x.SubjectId).HasColumnName("subject_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ConfigurationId, x.Status });
        b.HasOne(x => x.Configuration).WithMany(x => x.Runs).HasForeignKey(x => new { x.CompanyId, x.ConfigurationId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PresentationRun).WithMany().HasForeignKey(x => new { x.CompanyId, x.PresentationRunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
