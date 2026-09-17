using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPresentationPresetSlideConfiguration : IEntityTypeConfiguration<SalesPresentationPresetSlide>
{
    public void Configure(EntityTypeBuilder<SalesPresentationPresetSlide> b)
    {
        b.ToTable("sales_presentation_preset_slides", t => t.HasCheckConstraint("CK_sales_presentation_preset_slides_dimensions", "slide_number >= 1 AND processing_version >= 1 AND image_width_pixels > 0 AND image_height_pixels > 0 AND expected_duration_seconds > 0"));
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.AssetId).HasColumnName("asset_id"); b.Property(x => x.ProcessingVersion).HasColumnName("processing_version"); b.Property(x => x.SlideNumber).HasColumnName("slide_number");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(500); b.Property(x => x.ExtractedText).HasColumnName("extracted_text").HasMaxLength(16000); b.Property(x => x.SpeakerNotes).HasColumnName("speaker_notes").HasMaxLength(16000);
        b.Property(x => x.ImageStorageKey).HasColumnName("image_storage_key").HasMaxLength(1000); b.Property(x => x.ImageStorageUrl).HasColumnName("image_storage_url").HasMaxLength(2000); b.Property(x => x.ImageWidthPixels).HasColumnName("image_width_pixels"); b.Property(x => x.ImageHeightPixels).HasColumnName("image_height_pixels");
        b.Property(x => x.SourceWidthEmus).HasColumnName("source_width_emus"); b.Property(x => x.SourceHeightEmus).HasColumnName("source_height_emus"); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        b.Property(x => x.Objective).HasColumnName("objective").HasMaxLength(1000); b.Property(x => x.BaselineTalkingPoints).HasColumnName("baseline_talking_points").HasMaxLength(8000); b.Property(x => x.ExpectedDurationSeconds).HasColumnName("expected_duration_seconds"); b.Property(x => x.TransitionText).HasColumnName("transition_text").HasMaxLength(1000); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.AssetId, x.ProcessingVersion, x.SlideNumber }).IsUnique();
        b.HasOne(x => x.Asset).WithMany(x => x.Slides).HasForeignKey(nameof(SalesPresentationPresetSlide.CompanyId), nameof(SalesPresentationPresetSlide.AssetId)).HasPrincipalKey(nameof(SalesPresentationPresetAsset.CompanyId), nameof(SalesPresentationPresetAsset.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
