using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPresentationPresetVersionConfiguration : IEntityTypeConfiguration<SalesPresentationPresetVersion>
{
    public void Configure(EntityTypeBuilder<SalesPresentationPresetVersion> b)
    {
        b.ToTable("sales_presentation_preset_versions", t => t.HasCheckConstraint("CK_sales_presentation_preset_versions_duration", "duration_minutes BETWEEN 1 AND 480"));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.PresetId).HasColumnName("preset_id"); b.Property(x => x.VersionNumber).HasColumnName("version_number");
        b.Property(x => x.Lifecycle).HasColumnName("lifecycle").HasConversion(x => x.ToStorageValue(), x => SalesPresentationPresetEnumValues.ParseVersionLifecycle(x)).HasMaxLength(20);
        b.Property(x => x.DefaultPresenterAgentId).HasColumnName("default_presenter_agent_id"); b.Property(x => x.BehaviorSettingsJson).HasColumnName("behavior_settings_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.Goal).HasColumnName("goal").HasMaxLength(2000); b.Property(x => x.Audience).HasColumnName("audience").HasMaxLength(1000); b.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
        b.Property(x => x.DemoScenario).HasColumnName("demo_scenario").HasMaxLength(2000); b.Property(x => x.ControlMode).HasColumnName("control_mode").HasMaxLength(20); b.Property(x => x.Language).HasColumnName("language").HasMaxLength(20);
        b.Property(x => x.AllowSalesMeeting).HasColumnName("allow_sales_meeting"); b.Property(x => x.AllowCampaignActivity).HasColumnName("allow_campaign_activity"); b.Property(x => x.AllowAdHoc).HasColumnName("allow_ad_hoc"); b.Property(x => x.RequiredKnowledgeScope).HasColumnName("required_knowledge_scope").HasMaxLength(200);
        b.Property(x => x.PublishedByUserId).HasColumnName("published_by_user_id"); b.Property(x => x.PublishedUtc).HasColumnName("published_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").HasDefaultValue(1L).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.PresetId, x.VersionNumber }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.PresetId, x.Lifecycle });
        b.HasOne(x => x.Preset).WithMany(x => x.Versions).HasForeignKey(nameof(SalesPresentationPresetVersion.CompanyId), nameof(SalesPresentationPresetVersion.PresetId)).HasPrincipalKey(nameof(SalesPresentationPreset.CompanyId), nameof(SalesPresentationPreset.Id)).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DefaultPresenterAgent).WithMany().HasForeignKey(nameof(SalesPresentationPresetVersion.CompanyId), nameof(SalesPresentationPresetVersion.DefaultPresenterAgentId)).HasPrincipalKey(nameof(Agent.CompanyId), nameof(Agent.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
