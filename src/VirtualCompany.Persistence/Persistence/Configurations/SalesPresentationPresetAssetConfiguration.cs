using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPresentationPresetAssetConfiguration : IEntityTypeConfiguration<SalesPresentationPresetAsset>
{
    public void Configure(EntityTypeBuilder<SalesPresentationPresetAsset> b)
    {
        b.ToTable("sales_presentation_preset_assets", t => t.HasCheckConstraint("CK_sales_presentation_preset_assets_counts", "file_size_bytes > 0 AND slide_count >= 0 AND processing_attempt_count >= 0 AND processing_version >= 1"));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.PresetVersionId).HasColumnName("preset_version_id");
        b.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(260); b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(200); b.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64); b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(1000); b.Property(x => x.StorageUrl).HasColumnName("storage_url").HasMaxLength(2000);
        b.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => SalesPresentationPresetEnumValues.ParseAssetStatus(x)).HasMaxLength(32); b.Property(x => x.ProcessingVersion).HasColumnName("processing_version"); b.Property(x => x.ProcessingAttemptCount).HasColumnName("processing_attempt_count"); b.Property(x => x.SlideCount).HasColumnName("slide_count");
        b.Property(x => x.RendererName).HasColumnName("renderer_name").HasMaxLength(100); b.Property(x => x.RendererVersion).HasColumnName("renderer_version").HasMaxLength(50); b.Property(x => x.AnimationHandling).HasColumnName("animation_handling").HasMaxLength(100);
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.Property(x => x.CanRetry).HasColumnName("can_retry"); b.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.ProcessingStartedUtc).HasColumnName("processing_started_at"); b.Property(x => x.ProcessedUtc).HasColumnName("processed_at"); b.Property(x => x.FailedUtc).HasColumnName("failed_at"); b.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").HasDefaultValue(1L).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.PresetVersionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.ProcessingStartedUtc }); b.HasIndex(x => new { x.CompanyId, x.ContentHash, x.ProcessingVersion });
        b.HasOne(x => x.PresetVersion).WithOne(x => x.Asset).HasForeignKey<SalesPresentationPresetAsset>(x => new { x.CompanyId, x.PresetVersionId }).HasPrincipalKey<SalesPresentationPresetVersion>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
