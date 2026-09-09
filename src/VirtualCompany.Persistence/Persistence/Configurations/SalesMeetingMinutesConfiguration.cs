using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class SalesMeetingMinutesConfiguration : IEntityTypeConfiguration<SalesMeetingMinutes>
{
    public void Configure(EntityTypeBuilder<SalesMeetingMinutes> builder)
    {
        builder.ToTable("sales_meeting_minutes"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id"); builder.Property(x => x.GenerationRequestId).HasColumnName("generation_request_id");
        builder.Property(x => x.PreviousVersionId).HasColumnName("previous_version_id"); builder.Property(x => x.ArtifactVersion).HasColumnName("artifact_version");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasConversion(x => x.ToStorageValue(), x => SalesMeetingClosingEnumValues.ParseStatus(x));
        builder.Property(x => x.EvidenceCaptureVersion).HasColumnName("evidence_capture_version"); builder.Property(x => x.EvidenceCutoffUtc).HasColumnName("evidence_cutoff_utc");
        builder.Property(x => x.GeneratorAgentId).HasColumnName("generator_agent_id"); builder.Property(x => x.AiRunId).HasColumnName("ai_run_id");
        builder.Property(x => x.GeneratorVersion).HasColumnName("generator_version").HasMaxLength(100); builder.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(100);
        builder.Property(x => x.RetentionUntilUtc).HasColumnName("retention_until_utc"); builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id"); builder.Property(x => x.ReviewedUtc).HasColumnName("reviewed_at");
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id"); builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.Property(x => x.IsEvidenceStale).HasColumnName("is_evidence_stale").HasDefaultValue(false);
        builder.Property(x => x.EvidenceStaleUtc).HasColumnName("evidence_stale_at");
        builder.Property(x => x.EvidenceStaleReason).HasColumnName("evidence_stale_reason").HasMaxLength(500);
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.GenerationRequestId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.ArtifactVersion }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.Status, x.UpdatedUtc }); builder.HasIndex(x => new { x.CompanyId, x.RetentionUntilUtc });
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(x => new { x.CompanyId, x.SessionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.PreviousVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.PreviousVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.ToTable(t => { t.HasCheckConstraint("CK_sales_meeting_minutes_version", "artifact_version > 0"); t.HasCheckConstraint("CK_sales_meeting_minutes_capture_version", "evidence_capture_version >= 0"); });
    }
}

public sealed class SalesMeetingMinutesItemConfiguration : IEntityTypeConfiguration<SalesMeetingMinutesItem>
{
    public void Configure(EntityTypeBuilder<SalesMeetingMinutesItem> builder)
    {
        builder.ToTable("sales_meeting_minutes_items"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.MinutesId).HasColumnName("minutes_id");
        builder.Property(x => x.Order).HasColumnName("item_order"); builder.Property(x => x.ItemType).HasColumnName("item_type").HasMaxLength(48).HasConversion(x => x.ToStorageValue(), x => SalesMeetingClosingEnumValues.ParseMinutesItemType(x));
        builder.Property(x => x.Content).HasColumnName("content").HasMaxLength(4000); builder.Property(x => x.OwnerLabel).HasColumnName("owner_label").HasMaxLength(160);
        builder.Property(x => x.DueUtc).HasColumnName("due_at"); builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(500);
        builder.Property(x => x.SourceArtifactId).HasColumnName("source_artifact_id"); builder.Property(x => x.RequiresReview).HasColumnName("requires_review"); builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.HasIndex(x => new { x.CompanyId, x.MinutesId, x.Order }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.SourceArtifactId });
        builder.HasOne(x => x.Minutes).WithMany(x => x.Items).HasForeignKey(x => new { x.CompanyId, x.MinutesId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SourceArtifact).WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceArtifactId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.ToTable(t => t.HasCheckConstraint("CK_sales_meeting_minutes_items_order", "item_order >= 0"));
    }
}
