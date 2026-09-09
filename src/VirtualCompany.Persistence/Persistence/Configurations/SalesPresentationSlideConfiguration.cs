using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPresentationSlideConfiguration : IEntityTypeConfiguration<SalesPresentationSlide>
{
    public void Configure(EntityTypeBuilder<SalesPresentationSlide> builder)
    {
        builder.ToTable("sales_presentation_slides", table =>
        {
            table.HasCheckConstraint("CK_sales_presentation_slides_numbers", "processing_version >= 1 AND slide_number >= 1");
            table.HasCheckConstraint("CK_sales_presentation_slides_dimensions", "image_width_pixels > 0 AND image_height_pixels > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DeckId).HasColumnName("deck_id").IsRequired();
        builder.Property(x => x.ProcessingVersion).HasColumnName("processing_version").IsRequired();
        builder.Property(x => x.SlideNumber).HasColumnName("slide_number").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
        builder.Property(x => x.ExtractedText).HasColumnName("extracted_text").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.SpeakerNotes).HasColumnName("speaker_notes").HasMaxLength(16000);
        builder.Property(x => x.ImageStorageKey).HasColumnName("image_storage_key").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ImageStorageUrl).HasColumnName("image_storage_url").HasMaxLength(2000);
        builder.Property(x => x.ImageWidthPixels).HasColumnName("image_width_pixels").IsRequired();
        builder.Property(x => x.ImageHeightPixels).HasColumnName("image_height_pixels").IsRequired();
        builder.Property(x => x.SourceWidthEmus).HasColumnName("source_width_emus").IsRequired();
        builder.Property(x => x.SourceHeightEmus).HasColumnName("source_height_emus").IsRequired();
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Objective).HasColumnName("objective").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ExpectedDurationSeconds).HasColumnName("expected_duration_seconds").IsRequired();
        builder.Property(x => x.TransitionText).HasColumnName("transition_text").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => SalesPresentationSlideStatusValues.Parse(value))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.DeckId, x.ProcessingVersion, x.SlideNumber }).IsUnique();
        builder.HasOne(x => x.Deck).WithMany()
            .HasForeignKey(nameof(SalesPresentationSlide.CompanyId), nameof(SalesPresentationSlide.DeckId))
            .HasPrincipalKey(nameof(SalesPresentationDeck.CompanyId), nameof(SalesPresentationDeck.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
